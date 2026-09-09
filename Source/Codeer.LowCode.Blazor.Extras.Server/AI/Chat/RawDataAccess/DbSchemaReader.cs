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
