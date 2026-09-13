using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の索引 (書き込み専用列 = 通常の読み込み経路では SELECT されない) をモジュールの表から読む。
    /// 読むのは Id・文章・ベクトルと論理削除の列だけ (LoginAccountStore と同じく、書き込み専用列のための最小の直接読み取り)。
    /// </summary>
    internal static class SemanticSearchIndexReader
    {
        public sealed record Entry(string Id, string Text, float[] Vector);

        public static async Task<List<Entry>> ReadAsync(IDbAccessor db, ModuleDesign module, SemanticSearchFieldDesign field, CancellationToken cancellationToken)
        {
            var idColumn = module.Fields.OfType<IdFieldDesign>().FirstOrDefault()?.DbColumn;
            if (string.IsNullOrWhiteSpace(module.DbTable) || string.IsNullOrWhiteSpace(idColumn) || !field.HasColumns) return new();
            var logicalDelete = (module.Fields.FirstOrDefault(f => f.Name == SystemFieldNames.LogicalDelete) as DbValueFieldDesignBase)?.DbColumn;

            var q = Quote(db, module.DataSourceName);
            var columns = new List<string> { q(idColumn), q(field.DbColumnText), q(field.DbColumnVector) };
            if (!string.IsNullOrWhiteSpace(logicalDelete)) columns.Add(q(logicalDelete));
            var sql = $"select {string.Join(", ", columns)} from {q(module.DbTable)} where {q(field.DbColumnVector)} is not null";
            cancellationToken.ThrowIfCancellationRequested();
            var rows = await db.QueryAsync(module.DataSourceName, sql, new());

            var result = new List<Entry>();
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(logicalDelete) && IsTrue(Value(row, logicalDelete))) continue;
                var vector = SemanticSearchVector.Decode(Convert.ToString(Value(row, field.DbColumnVector)));
                if (vector == null) continue;
                result.Add(new Entry(Convert.ToString(Value(row, idColumn)) ?? string.Empty, Convert.ToString(Value(row, field.DbColumnText)) ?? string.Empty, vector));
            }
            return result;
        }

        //識別子の引用符は DB の種類で変わる
        static Func<string, string> Quote(IDbAccessor db, string dataSourceName)
        {
            var type = db.GetDataSource(dataSourceName)?.DataSourceType ?? throw new InvalidOperationException($"SemanticSearch: data source '{dataSourceName}' not found.");
            return x => type == DataSourceType.SQLServer ? $"[{x}]" : type == DataSourceType.MySQL ? $"`{x}`" : $"\"{x}\"";
        }

        static bool IsTrue(object? v) => v switch
        {
            null => false,
            bool b => b,
            string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
            _ => Convert.ToInt64(v) != 0,
        };

        static object? Value(IDictionary<string, object> row, string column)
        {
            if (row.TryGetValue(column, out var v)) return v is DBNull ? null : v;
            var key = row.Keys.FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase));
            return key == null || row[key] is DBNull ? null : row[key];
        }
    }
}
