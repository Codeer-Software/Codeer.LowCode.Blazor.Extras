using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の索引 (書き込み専用列 = 通常の読み込み経路では SELECT されない) を DB のベクトル検索で読む。
    /// 書き込み専用列は ModuleDataIO では読めないので IDbAccessor で直接 SELECT する (LoginAccountStore がパスワード列を読むのと同じ割り切り)。
    /// 距離計算は DB (pgvector / SQL Server 2025) に任せ、距離順の上位だけを読む。論理削除された行は除く。
    /// 対応 DB は <see cref="SupportsDbSearch"/>。それ以外の DB では意味検索は使えない (サーバーでの比較はしない)。
    /// </summary>
    internal static class SemanticSearchIndexReader
    {
        /// <summary>DB 側で距離順に読んだ 1 件 (Score はコサイン類似度 0〜1 に揃える)。</summary>
        public sealed record ScoredEntry(string Id, string Text, double Score);

        /// <summary>この DB 種別でベクトル検索 (距離関数) が使えるか。PostgreSQL は pgvector 拡張、SQL Server は 2025 以降が前提 (無ければ実行時エラー)。</summary>
        public static bool SupportsDbSearch(DataSourceType type)
            => type is DataSourceType.PostgreSQL or DataSourceType.SQLServer;

        /// <summary>DB のベクトル検索で、質問ベクトルに近い順に上位 top 件を読む。</summary>
        public static async Task<List<ScoredEntry>> SearchAsync(IDbAccessor db, ModuleDesign module, SemanticSearchFieldDesign field, float[] queryVector, int top, CancellationToken cancellationToken)
        {
            var idColumn = module.Fields.OfType<IdFieldDesign>().FirstOrDefault()?.DbColumn;
            if (string.IsNullOrWhiteSpace(module.DbTable) || string.IsNullOrWhiteSpace(idColumn) || !field.HasColumns) return new();
            var type = DataSourceTypeOf(db, module.DataSourceName);
            var sql = BuildSearchSql(type, module.DbTable, idColumn, field.DbColumnText, field.DbColumnVectorSearch, LogicalDeleteColumn(module), VectorLiteral(type, queryVector), top);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.QueryAsync(module.DataSourceName, sql, new());
            var result = new List<ScoredEntry>();
            foreach (var row in rows)
            {
                var score = Value(row, "semantic_score");
                result.Add(new ScoredEntry(
                    Convert.ToString(Value(row, idColumn)) ?? string.Empty,
                    Convert.ToString(Value(row, field.DbColumnText)) ?? string.Empty,
                    score == null ? 0 : Convert.ToDouble(score, CultureInfo.InvariantCulture)));
            }
            return result;
        }

        /// <summary>
        /// 距離順に上位 top 件を取る SELECT。列 semantic_score にコサイン類似度 (1 - コサイン距離) を出す。
        /// PostgreSQL: pgvector の <c>&lt;=&gt;</c> (コサイン距離)。SQL Server: <c>VECTOR_DISTANCE('cosine', …)</c>。
        /// </summary>
        public static string BuildSearchSql(DataSourceType type, string table, string idColumn, string textColumn, string vectorColumn, string? logicalDeleteColumn, string vectorLiteral, int top)
        {
            var q = Quote(type);
            var notDeleted = string.IsNullOrWhiteSpace(logicalDeleteColumn) ? "" : $" and ({q(logicalDeleteColumn)} is null or cast({q(logicalDeleteColumn)} as integer) = 0)";
            return type switch
            {
                DataSourceType.PostgreSQL =>
                    $"select {q(idColumn)}, {q(textColumn)}, 1 - ({q(vectorColumn)} <=> {vectorLiteral}) as semantic_score from {q(table)} " +
                    $"where {q(vectorColumn)} is not null{notDeleted} order by {q(vectorColumn)} <=> {vectorLiteral} limit {top}",
                DataSourceType.SQLServer =>
                    $"select top ({top}) {q(idColumn)}, {q(textColumn)}, 1 - VECTOR_DISTANCE('cosine', {q(vectorColumn)}, {vectorLiteral}) as semantic_score from {q(table)} " +
                    $"where {q(vectorColumn)} is not null{notDeleted} order by VECTOR_DISTANCE('cosine', {q(vectorColumn)}, {vectorLiteral})",
                _ => throw new NotSupportedException($"SemanticSearch: {type} does not support vector search in the database."),
            };
        }

        /// <summary>質問ベクトルを SQL に埋め込むリテラル (数値だけをサーバーが並べるので注入の余地はない)。execute_sql の {embed:…} の置換にも使う。</summary>
        public static string VectorLiteral(DataSourceType type, float[] vector)
        {
            var text = SemanticSearchVector.Encode(vector);
            return type switch
            {
                DataSourceType.PostgreSQL => $"'{text}'::vector",
                DataSourceType.SQLServer => $"CAST('{text}' AS VECTOR({vector.Length}))",
                _ => throw new NotSupportedException($"SemanticSearch: {type} does not support vector search in the database."),
            };
        }

        /// <summary>SQL の方言ごとの、ベクトル検索の書き方 (AI への説明用)。</summary>
        public static string? DialectHint(DataSourceType type) => type switch
        {
            DataSourceType.PostgreSQL => "pgvector: コサイン距離は `列 <=> {embed:…}` (0 に近いほど似ている)。似ている順は `ORDER BY 列 <=> {embed:…} LIMIT n`、類似度は `1 - (列 <=> {embed:…})`",
            DataSourceType.SQLServer => "SQL Server 2025: コサイン距離は `VECTOR_DISTANCE('cosine', 列, {embed:…})` (0 に近いほど似ている)。似ている順は `ORDER BY VECTOR_DISTANCE('cosine', 列, {embed:…})` と `TOP (n)`",
            _ => null,
        };

        internal static DataSourceType DataSourceTypeOf(IDbAccessor db, string dataSourceName)
            => db.GetDataSource(dataSourceName)?.DataSourceType ?? throw new InvalidOperationException($"SemanticSearch: data source '{dataSourceName}' not found.");

        static string? LogicalDeleteColumn(ModuleDesign module)
            => (module.Fields.FirstOrDefault(f => f.Name == SystemFieldNames.LogicalDelete) as DbValueFieldDesignBase)?.DbColumn;

        //識別子の引用符は DB の種類で変わる
        static Func<string, string> Quote(DataSourceType type)
            => x => type == DataSourceType.SQLServer ? $"[{x}]" : type == DataSourceType.MySQL ? $"`{x}`" : $"\"{x}\"";

        static object? Value(IDictionary<string, object> row, string column)
        {
            if (row.TryGetValue(column, out var v)) return v is DBNull ? null : v;
            var key = row.Keys.FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase));
            return key == null || row[key] is DBNull ? null : row[key];
        }
    }
}
