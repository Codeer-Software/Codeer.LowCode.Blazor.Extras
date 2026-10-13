using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の索引 (書き込み専用列 = 通常の読み込み経路では SELECT されない) を DB のベクトル検索で読む。
    /// 書き込み専用列は ModuleDataIO では読めないので IDbAccessor で直接 SELECT する (LoginAccountStore がパスワード列を読むのと同じ割り切り)。
    /// 距離計算は DB (pgvector / SQL Server 2025) に任せ、距離順の上位だけを読む。論理削除された行は除く。
    /// 対応 DB は <see cref="SemanticSearchVector.SupportsDbSearch"/>。それ以外の DB では意味検索は使えない (サーバーでの比較はしない)。
    /// </summary>
    internal static class SemanticSearchIndexReader
    {
        /// <summary>DB 側で距離順に読んだ 1 件 (Score はコサイン類似度 0〜1 に揃える)。</summary>
        public sealed record ScoredEntry(string Id, string Text, double Score);

        /// <summary>
        /// ベクトルが入っている行 (論理削除した行は除く) の Id (再索引の missingOnly が「まだ無い行」を決めるのに使う)。どの DB でも読める (テキスト列を見るだけ)。
        /// 複合 Id (Id の列が無い) では判定できないので空 = 全行が対象。
        /// </summary>
        public static async Task<HashSet<string>> ReadIndexedIdsAsync(IDbAccessor db, ModuleDesign module, SemanticSearchFieldDesign field, CancellationToken cancellationToken)
        {
            var idColumn = IdColumn(module);
            var result = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(module.DbTable) || string.IsNullOrWhiteSpace(idColumn) || string.IsNullOrWhiteSpace(field.DbColumnVector)) return result;
            var type = DataSourceTypeOf(db, module.DataSourceName);
            var q = Quote(type);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.QueryAsync(module.DataSourceName, $"select {q(idColumn)} from {Table(q, module.DbTable)} where {q(field.DbColumnVector)} is not null{NotDeletedSql(module, type)}", new());
            foreach (var row in rows)
            {
                var id = Convert.ToString(Value(row, idColumn), CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(id)) result.Add(id);
            }
            return result;
        }

        /// <summary>
        /// DB のベクトル検索で、質問ベクトルに近い順に上位 top 件を読む。
        /// dataSourceName は接続に使うデータソース (AI 用に別名の読み取り専用接続を作っている構成ではモジュールの DataSourceName と違う)。
        /// </summary>
        public static async Task<List<ScoredEntry>> SearchAsync(IDbAccessor db, string dataSourceName, ModuleDesign module, SemanticSearchFieldDesign field, float[] queryVector, int top, CancellationToken cancellationToken)
        {
            var idColumn = IdColumn(module);
            if (string.IsNullOrWhiteSpace(module.DbTable) || string.IsNullOrWhiteSpace(idColumn) || !field.HasColumns) return new();
            var type = DataSourceTypeOf(db, dataSourceName);
            var sql = BuildSearchSql(type, module.DbTable, idColumn, field.DbColumnText, field.DbColumnVectorSearch, NotDeletedSql(module, type), SemanticSearchVector.Literal(type, queryVector), top);
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.QueryAsync(dataSourceName, sql, new());
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
        /// 距離の式は <see cref="SemanticSearchVector.CosineDistance"/> (画面の検索欄・権限の効く検索と同じ)。
        /// </summary>
        public static string BuildSearchSql(DataSourceType type, string table, string idColumn, string textColumn, string vectorColumn, string notDeletedSql, string vectorLiteral, int top)
        {
            var q = Quote(type);
            var distance = SemanticSearchVector.CosineDistance(type, q(vectorColumn), vectorLiteral);
            var select = $"{q(idColumn)}, {q(textColumn)}, 1 - {distance} as semantic_score from {Table(q, table)} where {q(vectorColumn)} is not null{notDeletedSql} order by {distance}";
            return type == DataSourceType.SQLServer ? $"select top ({top}) {select}" : $"select {select} limit {top}";
        }

        /// <summary>
        /// 削除済みの行を除く WHERE の断片 (" and ..." か空)。判定の規則は本体の一覧検索 (SelectSqlCreator.CreateDeletedFilterCondition) と同じ:
        /// LogicalDelete があればそれ (NULL か偽を残す)、無ければ DeletedAt → Deleter の順 (NULL を残す)。
        /// </summary>
        public static string NotDeletedSql(ModuleDesign module, DataSourceType type)
        {
            var q = Quote(type);
            var logicalDelete = DbColumnOf(module, SystemFieldNames.LogicalDelete);
            if (!string.IsNullOrWhiteSpace(logicalDelete))
            {
                //MySQL の cast は integer を受け付けない (signed)
                var integer = type == DataSourceType.MySQL ? "signed" : "integer";
                return $" and ({q(logicalDelete)} is null or cast({q(logicalDelete)} as {integer}) = 0)";
            }
            var marker = DbColumnOf(module, SystemFieldNames.DeletedAt) ?? DbColumnOf(module, SystemFieldNames.Deleter);
            return string.IsNullOrWhiteSpace(marker) ? "" : $" and {q(marker)} is null";
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

        //Id の列 (本体と同じく名前 Id のフィールド)
        static string? IdColumn(ModuleDesign module)
            => module.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id)?.DbColumn;

        static string? DbColumnOf(ModuleDesign module, string fieldName)
            => (module.Fields.FirstOrDefault(f => f.Name == fieldName) as DbValueFieldDesignBase)?.DbColumn is { Length: > 0 } column ? column : null;

        //識別子の引用符は DB の種類で変わる
        //表名 (スキーマ付き "dbo.notes" は本体の SqlParts.Table と同じく . で分けて各部を引用)
        static string Table(Func<string, string> q, string table)
            => string.Join(".", table.Split('.').Select(q));

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
