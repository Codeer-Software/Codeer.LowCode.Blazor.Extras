using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
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
    internal sealed class RawDataAccessToolSet : IAIChatToolSet
    {
        static readonly Regex _forbidden = new(
            @"\b(INSERT|UPDATE|DELETE|MERGE|DROP|ALTER|CREATE|TRUNCATE|GRANT|REVOKE|EXEC|EXECUTE|CALL|ATTACH|DETACH|PRAGMA|VACUUM|REINDEX|REPLACE|UPSERT|INTO)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex _stringLiteral = new(@"'(?:[^']|'')*'", RegexOptions.Compiled);
        static readonly Regex _lineComment = new(@"--[^\r\n]*", RegexOptions.Compiled);
        static readonly Regex _blockComment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline);

        readonly Func<IDbAccessor> _dbAccessorFactory;
        readonly IModuleDesigns? _modules;
        readonly RawDataAccessOptions _options;
        readonly object _schemaLock = new();
        string? _schemaText;
        DateTime _schemaLoaded;

        /// <param name="dbAccessorFactory">データソースへ接続する IDbAccessor を作る (SQL 1 回ごとに作って捨てる。バックグラウンド実行のためリクエストの寿命に乗れない)。例: <c>() => new DbAccessor(SystemConfig.Instance.DataSources)</c></param>
        /// <param name="modules">モジュール定義。表と列に業務上の名前 (モジュール名・フィールド名・候補値) を添えてスキーマを説明する。null でも動く</param>
        /// <param name="options">データソース名と上限値</param>
        public RawDataAccessToolSet(Func<IDbAccessor> dbAccessorFactory, IModuleDesigns? modules, RawDataAccessOptions options)
        {
            if (string.IsNullOrEmpty(options.DataSourceName)) throw new ArgumentException("RawDataAccessOptions.DataSourceName is required.", nameof(options));
            _dbAccessorFactory = dbAccessorFactory;
            _modules = modules;
            _options = options;
        }

        public string Instructions
        {
            get
            {
                var sb = new StringBuilder();
                sb.AppendLine("アプリのデータベースに問い合わせることができます。");
                sb.AppendLine("- どの表や列があるか確かでないときは、先に get_schema を呼んでください。名前を推測してはいけません。");
                sb.AppendLine("- execute_sql は読み取り専用の SELECT を 1 文だけ実行します。それ以外は拒否され、DB ユーザーも読み取り専用です。");
                sb.AppendLine($"- 結果は絞ってください (最大 {_options.MaxRows} 行まで返ります)。生の行を取るより、集計 (GROUP BY, SUM, COUNT) を優先してください。");
                sb.AppendLine("- 結果は Markdown の表で示し、続けて短い解釈を書いてください。数値はクエリ結果に基づくもの以外を書かないこと。");
                sb.AppendLine("- クエリが失敗したらエラーを読み、SQL を直して再試行してください (数回まで)。");
                if (!string.IsNullOrWhiteSpace(_options.AdditionalInstructions)) sb.AppendLine().Append(_options.AdditionalInstructions.Trim());
                return sb.ToString();
            }
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                () => GetSchemaAsync(context),
                "get_schema",
                "問い合わせに使える表と列の一覧を返す。SQL の方言と、分かる範囲で業務上の名前を添える。");
            yield return AIFunctionFactory.Create(
                ([Description("この DB の方言で書いた、読み取り専用の SELECT 文 1 つ。")] string sql,
                 [Description("このクエリで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose)
                    => ExecuteSqlAsync(sql, purpose, context),
                "execute_sql",
                "SELECT 文を 1 つ実行し、列と行を JSON で返す。上限を超えた行は切り捨てられる (truncated=true)。");
        }

        async Task<string> GetSchemaAsync(AIChatToolContext context)
        {
            context.Progress.Report(Resources.AIChat_ReadingSchema);
            lock (_schemaLock)
            {
                if (_schemaText != null && DateTime.UtcNow - _schemaLoaded < _options.SchemaCacheDuration) return _schemaText;
            }
            await using var db = _dbAccessorFactory();
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
            sb.Append("SQL の方言: ").AppendLine(dialect);
            sb.AppendLine("表 (名前: 列)。アプリの設計に業務上の名前があるものは [ ] で添えています。");
            foreach (var table in byTable)
            {
                var info = modules.TryGetValue(TableNameOnly(table.Key), out var m) ? m : null;
                sb.Append("- ").Append(table.Key);
                if (info != null) sb.Append(" [モジュール ").Append(info.ModuleName).Append(']');
                sb.AppendLine(":");
                foreach (var column in table)
                {
                    sb.Append("    ").Append(column.Name).Append(' ').Append(column.DataType);
                    if (info != null && info.Columns.TryGetValue(column.Name, out var field)) sb.Append("  [").Append(field).Append(']');
                    sb.AppendLine();
                }
            }
            if (byTable.Count == 0) sb.AppendLine("(この DB ユーザーから見える表はありません)");
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
            if (_modules == null) return result;
            foreach (var name in _modules.GetModuleNames())
            {
                var module = _modules.Find(name);
                if (module == null || string.IsNullOrEmpty(module.DbTable) || result.ContainsKey(module.DbTable)) continue;
                var info = new ModuleInfo { ModuleName = module.Name };
                foreach (var field in module.Fields)
                {
                    var column = (field as DbValueFieldDesignBase)?.DbColumn;
                    if (string.IsNullOrEmpty(column)) continue;
                    var description = field.Name;
                    if (field is SelectFieldDesign select && select.Candidates.Count > 0)
                        description += "; 候補値: " + string.Join(", ", select.Candidates.Select(c => c.Replace("\r", " ").Replace("\n", " ")));
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

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_RunningQuery : purpose.Trim());
            context.Logger?.LogInformation("AIChat RawDataAccess SQL by {User} (conversation {Conversation}): {Sql}", context.Request.UserName, context.Request.ConversationId, sql);

            try
            {
                await using var db = _dbAccessorFactory();
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
        internal static string? Validate(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return "SQL が空です。";
            var stripped = _blockComment.Replace(_lineComment.Replace(sql, " "), " ");
            var withoutLiterals = _stringLiteral.Replace(stripped, "''");
            if (withoutLiterals.Contains(';')) return "実行できるのは 1 文だけです。";
            var head = withoutLiterals.TrimStart();
            if (!head.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && !head.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
                return "実行できるのは SELECT 文だけです。";
            var match = _forbidden.Match(withoutLiterals);
            if (match.Success) return $"実行できるのは読み取り専用の SELECT 文だけです ({match.Value.ToUpperInvariant()} は使えません)。";
            return null;
        }
    }
}
