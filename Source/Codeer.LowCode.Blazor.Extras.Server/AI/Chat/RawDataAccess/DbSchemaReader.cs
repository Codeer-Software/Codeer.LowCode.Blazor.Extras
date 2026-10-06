using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Data.Common;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>接続ユーザーに見える表と列を DB から読む (DB 種別ごとのカタログ問い合わせ)。</summary>
    internal static class DbSchemaReader
    {
        public sealed record Column(string Table, string Name, string DataType);

        public static async Task<List<Column>> ReadAsync(IDbAccessor db, string dataSourceName, int timeoutSeconds, CancellationToken cancellationToken)
        {
            var dataSource = db.GetDataSource(dataSourceName) ?? throw new InvalidOperationException($"Data source '{dataSourceName}' is not defined.");
            var sql = dataSource.DataSourceType switch
            {
                DataSourceType.SQLServer =>
                    "SELECT TABLE_SCHEMA + '.' + TABLE_NAME AS t, COLUMN_NAME AS c, DATA_TYPE AS d " +
                    "FROM INFORMATION_SCHEMA.COLUMNS ORDER BY TABLE_SCHEMA, TABLE_NAME, ORDINAL_POSITION",
                DataSourceType.PostgreSQL =>
                    "SELECT CASE WHEN table_schema = 'public' THEN table_name ELSE table_schema || '.' || table_name END AS t, column_name AS c, data_type AS d " +
                    "FROM information_schema.columns WHERE table_schema NOT IN ('pg_catalog', 'information_schema') " +
                    "ORDER BY table_schema, table_name, ordinal_position",
                DataSourceType.MySQL =>
                    "SELECT table_name AS t, column_name AS c, data_type AS d FROM information_schema.columns " +
                    "WHERE table_schema = DATABASE() ORDER BY table_name, ordinal_position",
                DataSourceType.Oracle =>
                    "SELECT TABLE_NAME AS t, COLUMN_NAME AS c, DATA_TYPE AS d FROM USER_TAB_COLUMNS ORDER BY TABLE_NAME, COLUMN_ID",
                DataSourceType.SQLite =>
                    "SELECT m.name AS t, p.name AS c, p.type AS d FROM sqlite_master m JOIN pragma_table_info(m.name) p " +
                    "WHERE m.type IN ('table', 'view') AND m.name NOT LIKE 'sqlite_%' ORDER BY m.name, p.cid",
                _ => throw new NotSupportedException(dataSource.DataSourceType.ToString()),
            };

            var connection = db.GetConnection(dataSourceName);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = timeoutSeconds;
            command.Transaction = db.GetTransaction(dataSourceName) as DbTransaction;
            var columns = new List<Column>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(new Column(
                    reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0)) ?? string.Empty,
                    reader.IsDBNull(1) ? string.Empty : Convert.ToString(reader.GetValue(1)) ?? string.Empty,
                    reader.IsDBNull(2) ? string.Empty : Convert.ToString(reader.GetValue(2)) ?? string.Empty));
            }
            return columns;
        }

        /// <summary>表の概算行数 (統計。不明なら null) と、各索引の先頭列。</summary>
        public sealed record TableStats(long? Rows, IReadOnlyList<string> IndexColumns);

        /// <summary>
        /// 表ごとの概算行数と索引の先頭列を DB の統計・カタログから読む (COUNT(*) はしない)。キーは <see cref="Column.Table"/> と同じ形。
        /// 行数と索引は別々に読み、読めなかった (権限が無い等) ほうは空のまま返す (例外にしない)。SQLite は統計が無いので空。
        /// </summary>
        /// <param name="dbAccessorFactory">クエリごとに作って捨てる (失敗したクエリの後に同じ接続を使わない)</param>
        public static async Task<Dictionary<string, TableStats>> ReadTableStatsAsync(Func<IDbAccessor> dbAccessorFactory, string dataSourceName,
            int timeoutSeconds, Action<Exception>? onError, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, TableStats>(StringComparer.OrdinalIgnoreCase);
            DataSourceType type;
            await using (var db = dbAccessorFactory())
            {
                type = (db.GetDataSource(dataSourceName) ?? throw new InvalidOperationException($"Data source '{dataSourceName}' is not defined.")).DataSourceType;
            }
            var (rowsSql, indexSql) = StatsSql(type);
            if (rowsSql == null) return result;

            var rows = new Dictionary<string, long?>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var (table, value) in await ReadPairsAsync(dbAccessorFactory, dataSourceName, rowsSql, timeoutSeconds, cancellationToken))
                {
                    var count = value == null ? (long?)null : Convert.ToInt64(value);
                    rows[table] = count < 0 ? null : count;   //PostgreSQL の reltuples は未解析だと -1
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e) { onError?.Invoke(e); }

            var indexes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (indexSql != null)
            {
                try
                {
                    foreach (var (table, value) in await ReadPairsAsync(dbAccessorFactory, dataSourceName, indexSql, timeoutSeconds, cancellationToken))
                    {
                        var column = Convert.ToString(value) ?? string.Empty;
                        if (column.Length == 0) continue;
                        if (!indexes.TryGetValue(table, out var list)) indexes[table] = list = new List<string>();
                        if (!list.Contains(column, StringComparer.OrdinalIgnoreCase)) list.Add(column);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception e) { onError?.Invoke(e); }
            }

            foreach (var table in rows.Keys.Union(indexes.Keys, StringComparer.OrdinalIgnoreCase))
            {
                rows.TryGetValue(table, out var count);
                result[table] = new TableStats(count, indexes.TryGetValue(table, out var list) ? list : Array.Empty<string>());
            }
            return result;
        }

        //(行数, 索引の先頭列) のカタログ問い合わせ。どちらも 1 列目が表名 (ReadAsync と同じ形)、2 列目が値
        static (string? Rows, string? Indexes) StatsSql(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer => (
                "SELECT s.name + '.' + t.name AS t, SUM(p.rows) AS r FROM sys.tables t " +
                "JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) GROUP BY s.name, t.name",
                "SELECT s.name + '.' + t.name AS t, c.name AS c FROM sys.indexes i " +
                "JOIN sys.tables t ON t.object_id = i.object_id JOIN sys.schemas s ON s.schema_id = t.schema_id " +
                "JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal = 1 " +
                "JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " +
                "WHERE i.type IN (1, 2) AND i.is_disabled = 0 AND i.is_hypothetical = 0 ORDER BY s.name, t.name, i.index_id"),
            DataSourceType.PostgreSQL => (
                "SELECT CASE WHEN n.nspname = 'public' THEN c.relname ELSE n.nspname || '.' || c.relname END AS t, c.reltuples::bigint AS r " +
                "FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
                "WHERE c.relkind IN ('r', 'p', 'm') AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'",
                "SELECT CASE WHEN n.nspname = 'public' THEN t.relname ELSE n.nspname || '.' || t.relname END AS t, a.attname::text AS c " +
                "FROM pg_index x JOIN pg_class t ON t.oid = x.indrelid JOIN pg_namespace n ON n.oid = t.relnamespace " +
                "JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = x.indkey[0] " +
                "WHERE x.indisvalid AND x.indkey[0] <> 0 AND n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%' " +
                "ORDER BY 1, x.indisprimary DESC"),
            DataSourceType.MySQL => (
                "SELECT table_name AS t, table_rows AS r FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'",
                "SELECT table_name AS t, column_name AS c FROM information_schema.statistics WHERE table_schema = DATABASE() AND seq_in_index = 1 " +
                "ORDER BY table_name, CASE WHEN index_name = 'PRIMARY' THEN 0 ELSE 1 END, index_name"),
            DataSourceType.Oracle => (
                "SELECT TABLE_NAME AS t, NUM_ROWS AS r FROM USER_TABLES",
                "SELECT TABLE_NAME AS t, COLUMN_NAME AS c FROM USER_IND_COLUMNS WHERE COLUMN_POSITION = 1 ORDER BY TABLE_NAME, INDEX_NAME"),
            _ => (null, null),
        };

        static async Task<List<(string Table, object? Value)>> ReadPairsAsync(Func<IDbAccessor> dbAccessorFactory, string dataSourceName, string sql, int timeoutSeconds, CancellationToken cancellationToken)
        {
            await using var db = dbAccessorFactory();
            var connection = db.GetConnection(dataSourceName);
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = timeoutSeconds;
            command.Transaction = db.GetTransaction(dataSourceName) as DbTransaction;
            var result = new List<(string, object?)>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = reader.IsDBNull(0) ? string.Empty : Convert.ToString(reader.GetValue(0)) ?? string.Empty;
                if (table.Length == 0) continue;
                result.Add((table, reader.IsDBNull(1) ? null : reader.GetValue(1)));
            }
            return result;
        }

        /// <summary>SQL の方言名 (プロンプト用)。</summary>
        public static string DialectName(DataSourceType type) => type switch
        {
            DataSourceType.SQLServer => "Microsoft SQL Server (T-SQL。行数制限は TOP か OFFSET/FETCH)",
            DataSourceType.PostgreSQL => "PostgreSQL (行数制限は LIMIT。大文字小文字が混ざる識別子は二重引用符で囲む)",
            DataSourceType.MySQL => "MySQL (行数制限は LIMIT)",
            DataSourceType.Oracle => "Oracle (行数制限は FETCH FIRST n ROWS ONLY)",
            DataSourceType.SQLite => "SQLite (行数制限は LIMIT。日付はテキストで格納)",
            _ => type.ToString(),
        };
    }
}
