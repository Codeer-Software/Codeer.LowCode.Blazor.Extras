using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools
{
    /// <summary>
    /// AI に DB を直接読ませるツール群: <c>get_schema</c> (表と列。モジュール定義があれば業務上の名前を添える) と
    /// <c>execute_sql</c> (SELECT を実行して結果を返す)。集計や横断的な問い合わせを自由に組めるのが利点。
    /// <para>
    /// 何が読めるかは <see cref="RawDataAccessOptions.DataSourceName"/> の接続 (= AI 用の DB ユーザー) で決まる。
    /// ここでの SELECT 判定は補助で、書き込み拒否の本体は DB 側の権限に置く。行数・文字数・時間の上限と、
    /// 実行した SQL のログ (<see cref="AIChatToolContext.Logger"/>) はこのクラスが担う。
    /// </para>
    /// </summary>
    public class RawDataAccessToolSet : IAIChatToolSet
    {
        static readonly Regex _forbidden = new(
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL|ATTACH|DETACH|PRAGMA|VACUUM|REINDEX|REPLACE|UPSERT|INTO)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex _stringLiteral = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);
        static readonly Regex _lineComment = new(@"--[^\r\n]*", RegexOptions.Compiled);
        static readonly Regex _blockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        readonly RawDataAccessOptions _options;
        readonly object _schemaLock = new();
        string? _schemaText;
        DateTime _schemaLoaded;

        public RawDataAccessToolSet(RawDataAccessOptions options) => _options = options;

        public RawDataAccessOptions Options => _options;

        public string Instructions
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("You can query the application's database.");
                sb.AppendLine("- Call get_schema first when you are not sure which tables or columns exist. Never guess names.");
                sb.AppendLine("- execute_sql runs exactly one read-only SELECT statement. Anything else is rejected, and the database user is read-only.");
                sb.AppendLine($"- Limit result sets (at most {_options.MaxRows} rows are returned). Prefer aggregation (GROUP BY, SUM, COUNT) over fetching raw rows.");
                sb.AppendLine("- Present results as a Markdown table, then a short interpretation. Numbers must come from query results; never invent values.");
                sb.AppendLine("- If a query fails, read the error, fix the SQL and retry (at most a few times).");
                if (!string.IsNullOrWhiteSpace(_options.AdditionalInstructions)) sb.AppendLine().Append(_options.AdditionalInstructions.Trim());
                return sb.ToString();
            }
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                () => GetSchemaAsync(context),
                "get_schema",
                "Lists the tables and columns the assistant may query, with the SQL dialect and business names where known.");
            yield return AIFunctionFactory.Create(
                ([Description("A single read-only SELECT statement in the database's SQL dialect.")] string sql,
                 [Description("One sentence: what this query answers (shown to the user as progress).")] string purpose)
                    => ExecuteSqlAsync(sql, purpose, context),
                "execute_sql",
                "Runs one SELECT statement and returns the columns and rows as JSON. Rows beyond the limit are cut off (truncated=true).");
        }

        async Task<string> GetSchemaAsync(AIChatToolContext context)
        {
            context.Progress.Report("Reading the schema…");
            lock (_schemaLock)
            {
                if (_schemaText != null && DateTime.UtcNow - _schemaLoaded < _options.SchemaCacheDuration) return _schemaText;
            }
            await using var db = _options.DbAccessorFactory();
            var dataSource = db.GetDataSource(_options.DataSourceName) ?? throw new InvalidOperationException($"Data source '{_options.DataSourceName}' is not defined.");
            var columns = await DbSchemaReader.ReadAsync(db, _options.DataSourceName, _options.CommandTimeoutSeconds, context.CancellationToken);
            var text = FormatSchema(DbSchemaReader.DialectName(dataSource.DataSourceType), columns);
            lock (_schemaLock)
            {
                _schemaText = text;
                _schemaLoaded = DateTime.UtcNow;
            }
            context.Logger?.LogInformation("AIChat RawDataAccess schema read by {User}: {Tables} tables", context.Request.UserName, columns.Select(c => c.Table).Distinct().Count());
            return text;
        }

        string FormatSchema(string dialect, List<DbSchemaReader.Column> columns)
        {
            var excluded = new HashSet<string>(_options.ExcludedTables, StringComparer.OrdinalIgnoreCase);
            var byTable = columns.Where(c => !excluded.Contains(c.Table) && !excluded.Contains(TableNameOnly(c.Table)))
                .GroupBy(c => c.Table).ToList();
            var modules = ModuleInfoByTable();

            var sb = new StringBuilder();
            sb.Append("SQL dialect: ").AppendLine(dialect);
            sb.AppendLine("Tables (name: columns). Business names from the application design are given in brackets where known.");
            foreach (var table in byTable)
            {
                var info = modules.TryGetValue(TableNameOnly(table.Key), out var m) ? m : null;
                sb.Append("- ").Append(table.Key);
                if (info != null) sb.Append(" [module ").Append(info.ModuleName).Append(']');
                sb.AppendLine(":");
                foreach (var column in table)
                {
                    sb.Append("    ").Append(column.Name).Append(' ').Append(column.DataType);
                    if (info != null && info.Columns.TryGetValue(column.Name, out var field)) sb.Append("  [").Append(field).Append(']');
                    sb.AppendLine();
                }
            }
            if (byTable.Count == 0) sb.AppendLine("(no tables are visible to this database user)");
            return sb.ToString();
        }

        static string TableNameOnly(string table)
        {
            var index = table.LastIndexOf('.');
            return index < 0 ? table : table[(index + 1)..];
        }

        sealed class ModuleInfo
        {
            public string ModuleName = string.Empty;
            public Dictionary<string, string> Columns = new(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, ModuleInfo> ModuleInfoByTable()
        {
            var result = new Dictionary<string, ModuleInfo>(StringComparer.OrdinalIgnoreCase);
            if (_options.Modules == null) return result;
            foreach (var name in _options.Modules.GetModuleNames())
            {
                var module = _options.Modules.Find(name);
                if (module == null || string.IsNullOrEmpty(module.DbTable) || result.ContainsKey(module.DbTable)) continue;
                var info = new ModuleInfo { ModuleName = module.Name };
                foreach (var field in module.Fields)
                {
                    var column = (field as DbValueFieldDesignBase)?.DbColumn;
                    if (string.IsNullOrEmpty(column)) continue;
                    var description = field.Name;
                    if (field is SelectFieldDesign select && select.Candidates.Count > 0)
                        description += "; values: " + string.Join(", ", select.Candidates.Select(c => c.Replace("\r", " ").Replace("\n", " ")));
                    info.Columns[column] = description;
                }
                result[module.DbTable] = info;
            }
            return result;
        }

        async Task<string> ExecuteSqlAsync(string sql, string purpose, AIChatToolContext context)
        {
            sql = (sql ?? string.Empty).Trim().TrimEnd(';').Trim();
            var rejection = Validate(sql);
            if (rejection != null)
            {
                context.Logger?.LogWarning("AIChat RawDataAccess rejected SQL by {User}: {Reason} / {Sql}", context.Request.UserName, rejection, sql);
                return JsonSerializer.Serialize(new { error = rejection });
            }

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? "Running a query…" : purpose.Trim());
            context.Logger?.LogInformation("AIChat RawDataAccess SQL by {User} (conversation {Conversation}): {Sql}", context.Request.UserName, context.Request.ConversationId, sql);

            try
            {
                await using var db = _options.DbAccessorFactory();
                var connection = db.GetConnection(_options.DataSourceName);
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Transaction = db.GetTransaction(_options.DataSourceName) as DbTransaction;
                using var reader = await command.ExecuteReaderAsync(context.CancellationToken);

                var columns = new List<string>();
                for (var i = 0; i < reader.FieldCount; i++) columns.Add(reader.GetName(i));
                var rows = new List<object?[]>();
                var truncated = false;
                while (await reader.ReadAsync(context.CancellationToken))
                {
                    if (rows.Count >= _options.MaxRows) { truncated = true; break; }
                    var row = new object?[reader.FieldCount];
                    for (var i = 0; i < reader.FieldCount; i++) row[i] = ToJsonValue(reader.IsDBNull(i) ? null : reader.GetValue(i));
                    rows.Add(row);
                }
                return Serialize(columns, rows, truncated);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                context.Logger?.LogWarning("AIChat RawDataAccess SQL failed: {Message}", e.Message);
                return JsonSerializer.Serialize(new { error = e.Message });
            }
        }

        string Serialize(List<string> columns, List<object?[]> rows, bool truncated)
        {
            while (true)
            {
                var json = JsonSerializer.Serialize(new { columns, rowCount = rows.Count, truncated, rows });
                if (json.Length <= _options.MaxResultChars || rows.Count == 0) return json;
                rows.RemoveRange(rows.Count / 2, rows.Count - rows.Count / 2);
                truncated = true;
            }
        }

        static object? ToJsonValue(object? value) => value switch
        {
            null => null,
            DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss"),
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            TimeOnly t => t.ToString("HH:mm:ss"),
            TimeSpan t => t.ToString(),
            byte[] b => $"<binary {b.Length} bytes>",
            Guid g => g.ToString(),
            _ => value,
        };

        /// <summary>1 文の SELECT だけを通す簡易判定 (本体の防御は DB ユーザーの権限)。拒否理由を返し、通るなら null。</summary>
        public static string? Validate(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "SQL is empty.";
            var stripped = _blockComment.Replace(_lineComment.Replace(sql, " "), " ");
            var withoutLiterals = _stringLiteral.Replace(stripped, "''");
            if (withoutLiterals.Contains(';')) return "Only one statement is allowed.";
            var head = withoutLiterals.TrimStart();
            if (!head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                return "Only SELECT statements are allowed.";
            var match = _forbidden.Match(withoutLiterals);
            if (match.Success) return $"Only read-only SELECT statements are allowed ({match.Value.ToUpperInvariant()} is not permitted).";
            return null;
        }
    }
}
