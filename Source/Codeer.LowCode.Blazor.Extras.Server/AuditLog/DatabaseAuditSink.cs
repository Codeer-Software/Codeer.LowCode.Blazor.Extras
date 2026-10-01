using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// DB のテーブルへ書く出力先。列は固定 (<see cref="CreateTableSql"/>)。
    /// 書き込みは操作のトランザクションとは別の接続で行う (操作が失敗・ロールバックしても記録は残る)。
    /// </summary>
    public class DatabaseAuditSink : IAuditSink
    {
        static readonly JsonSerializerOptions TargetsJson = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

        readonly AuditLogDatabaseSettings _settings;
        readonly Func<IDbAccessor> _createDbAccessor;

        /// <param name="createDbAccessor">書き込みごとに作る接続 (例 <c>() => new DbAccessor(dataSources)</c>)。使い終わったら破棄する。</param>
        public DatabaseAuditSink(AuditLogDatabaseSettings settings, Func<IDbAccessor> createDbAccessor)
        {
            _settings = settings;
            _createDbAccessor = createDbAccessor;
        }

        public async Task WriteAsync(AuditEvent e)
        {
            await using var db = _createDbAccessor();
            var d = new Dialect(DataSourceType(db));
            var columns = new (string Column, object? Value)[]
            {
                ("occurred_at_utc", DbDateTime(e.OccurredAtUtc)),
                ("category", e.Category.ToString()),
                ("action", Truncate(e.Action, 128)),
                ("result", e.Result.ToString()),
                ("user_id", Truncate(e.UserId, 256)),
                ("client_ip", Truncate(e.ClientIp, 64)),
                ("user_agent", Truncate(e.UserAgent, 512)),
                ("request_id", Truncate(e.RequestId, 64)),
                ("host", Truncate(e.Host, 128)),
                ("design_version", Truncate(e.DesignVersion, 64)),
                ("targets", JsonSerializer.Serialize(e.Targets, TargetsJson)),
                ("detail", e.Detail),
            };
            var args = new Dictionary<string, object?>();
            var names = new List<string>();
            var values = new List<string>();
            foreach (var (column, value) in columns)
            {
                var p = d.Parameter(args.Count + 1);
                names.Add(d.Quote(column));
                values.Add(p);
                args[p] = value;
            }
            var sql = $"insert into {d.Quote(_settings.Table)} ({string.Join(", ", names)}) values ({string.Join(", ", values)})";
            await db.ExecuteAsync(_settings.DataSourceName, sql, args);
        }

        public async Task<int> PurgeAsync(DateTime olderThanUtc)
        {
            await using var db = _createDbAccessor();
            var d = new Dialect(DataSourceType(db));
            var p = d.Parameter(1);
            var sql = $"delete from {d.Quote(_settings.Table)} where {d.Quote("occurred_at_utc")} < {p}";
            return await db.ExecuteAsync(_settings.DataSourceName, sql, new() { [p] = DbDateTime(olderThanUtc) });
        }

        /// <summary>テーブルを作る SQL。列名は固定で、アプリからは INSERT しかしない。</summary>
        public static string CreateTableSql(DataSourceType type, string table)
        {
            var d = new Dialect(type);
            string id = type switch
            {
                SystemSettings.DataSourceType.SQLServer => "bigint identity(1,1) primary key",
                SystemSettings.DataSourceType.PostgreSQL => "bigserial primary key",
                SystemSettings.DataSourceType.MySQL => "bigint auto_increment primary key",
                SystemSettings.DataSourceType.Oracle => "number generated always as identity primary key",
                _ => "integer primary key autoincrement",
            };
            string dateTime = type switch
            {
                SystemSettings.DataSourceType.SQLServer => "datetime2",
                SystemSettings.DataSourceType.PostgreSQL => "timestamp",
                SystemSettings.DataSourceType.MySQL => "datetime(3)",
                SystemSettings.DataSourceType.Oracle => "timestamp",
                _ => "text",
            };
            string Varchar(int n) => type switch
            {
                SystemSettings.DataSourceType.SQLServer => $"nvarchar({n})",
                SystemSettings.DataSourceType.Oracle => $"varchar2({n} char)",
                SystemSettings.DataSourceType.SQLite => "text",
                _ => $"varchar({n})",
            };
            //対象 (行ごとの Id) と理由は長さに上限が無い。MySQL の text は 64KB までなので longtext にする
            string text = type switch
            {
                SystemSettings.DataSourceType.SQLServer => "nvarchar(max)",
                SystemSettings.DataSourceType.MySQL => "longtext",
                SystemSettings.DataSourceType.Oracle => "clob",
                _ => "text",
            };
            return $"""
                create table {d.Quote(table)} (
                  {d.Quote("id")} {id},
                  {d.Quote("occurred_at_utc")} {dateTime} not null,
                  {d.Quote("category")} {Varchar(32)} not null,
                  {d.Quote("action")} {Varchar(128)} not null,
                  {d.Quote("result")} {Varchar(16)} not null,
                  {d.Quote("user_id")} {Varchar(256)},
                  {d.Quote("client_ip")} {Varchar(64)},
                  {d.Quote("user_agent")} {Varchar(512)},
                  {d.Quote("request_id")} {Varchar(64)},
                  {d.Quote("host")} {Varchar(128)},
                  {d.Quote("design_version")} {Varchar(64)},
                  {d.Quote("targets")} {text},
                  {d.Quote("detail")} {text}
                )
                """;
        }

        DataSourceType DataSourceType(IDbAccessor db)
            => db.GetDataSource(_settings.DataSourceName)?.DataSourceType
               ?? throw new InvalidOperationException($"Audit log data source '{_settings.DataSourceName}' (AuditLogDatabase.DataSourceName) does not exist.");

        //UTC の時刻をそのまま列に入れる。Kind=Utc のまま渡すと PostgreSQL (Npgsql) は timestamptz として送り、
        //timestamp 列へはセッションのタイムゾーンに直して入る (サーバーが Asia/Tokyo なら JST になる)
        static DateTime DbDateTime(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Unspecified);

        static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

        readonly struct Dialect
        {
            readonly DataSourceType _type;
            public Dialect(DataSourceType type) => _type = type;
            public string Quote(string name) => _type switch
            {
                SystemSettings.DataSourceType.SQLServer => $"[{name}]",
                SystemSettings.DataSourceType.MySQL => $"`{name}`",
                _ => $"\"{name}\"",
            };
            public string Parameter(int index) => (_type == SystemSettings.DataSourceType.Oracle ? ":p" : "@p") + index;
        }
    }
}
