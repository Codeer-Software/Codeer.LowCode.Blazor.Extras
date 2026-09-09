using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
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
    /// AI に DB を直接読ませるツール群: <c>get_schema</c> (データソースごとの表と列。モジュール定義があれば業務上の名前を添える) と
    /// <c>execute_sql</c> (指定したデータソースで SELECT を実行して結果を返す)。集計や横断的な問い合わせを自由に組めるのが利点。
    /// <para>
    /// 何が読めるかは <see cref="RawDataAccessOptions.DataSourceNames"/> の接続 (= AI 用の DB ユーザー) で決まる。
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
        readonly Func<DesignData?>? _design;
        readonly RawDataAccessOptions _options;
        readonly List<string> _dataSourceNames;
        readonly object _schemaLock = new();
        Dictionary<string, List<DbSchemaReader.Column>>? _schemaCache;   //データソース名 → 列 (表・列・型)
        DateTime _schemaLoaded;
        string? _dialects;                                              //プロンプト用: データソースごとの方言 (接続はしない)

        /// <param name="dbAccessorFactory">データソースへ接続する IDbAccessor を作る (SQL 1 回ごとに作って捨てる。バックグラウンド実行のためリクエストの寿命に乗れない)。例: <c>() => new DbAccessor(SystemConfig.Instance.DataSources)</c></param>
        /// <param name="design">デザイン定義 (ホットリロードで変わるので都度取る)。表と列に業務上の名前 (モジュール名・フィールド名・候補値) を添えてスキーマを説明する。null でも動く</param>
        /// <param name="options">データソース名と上限値</param>
        public RawDataAccessToolSet(Func<IDbAccessor> dbAccessorFactory, Func<DesignData?>? design, RawDataAccessOptions options)
        {
            _dataSourceNames = (options.DataSourceNames ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (_dataSourceNames.Count == 0) throw new ArgumentException("RawDataAccessOptions.DataSourceNames is required.", nameof(options));
            _dbAccessorFactory = dbAccessorFactory;
            _design = design;
            _options = options;
        }

        public string GetInstructions(AIChatToolContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine("アプリのデータベースに問い合わせることができます。");
            sb.Append("- データソースと SQL の方言: ").AppendLine(Dialects());
            if (_dataSourceNames.Count > 1)
                sb.AppendLine("- 1 つの SQL は 1 つのデータソースにしか届きません (データソースをまたぐ JOIN はできません)。またがる質問は、データソースごとに問い合わせて結果を自分で突き合わせてください。execute_sql には dataSource を指定します。");
            sb.AppendLine("- 表と列を知る順番: まず補足文書とモジュール定義 (describe_module に表名と DB 列が出ます)。それで足りるなら get_schema は呼ばないでください。設計に無い表 (レガシー等) や、列名が確かでないときだけ get_schema を使います。");
            sb.AppendLine("- get_schema は引数なしなら表名の目次だけ (小さい)、tables を指定するとその表の列だけを返します。全表の列を一度に取ろうとしないでください。名前を推測してはいけません。");
            sb.AppendLine("- execute_sql は読み取り専用の SELECT を 1 文だけ実行します。それ以外は拒否され、DB ユーザーも読み取り専用です。");
            sb.AppendLine($"- 結果は絞ってください (最大 {_options.MaxRows} 行まで返ります)。生の行を取るより、集計 (GROUP BY, SUM, COUNT) を優先してください。");
            sb.AppendLine("- 結果は Markdown の表で示し、続けて短い解釈を書いてください。数値はクエリ結果に基づくもの以外を書かないこと。");
            sb.AppendLine("- クエリが失敗したらエラーを読み、SQL を直して再試行してください (数回まで)。");
            if (!string.IsNullOrWhiteSpace(_options.AdditionalInstructions)) sb.AppendLine().Append(_options.AdditionalInstructions.Trim());
            return sb.ToString();
        }

        //"Main = PostgreSQL (…), Archive = SQLite (…)" のような一覧。DataSource の種別だけ見る (接続はしない)
        string Dialects()
        {
            if (_dialects != null) return _dialects;
            var db = _dbAccessorFactory();
            try
            {
                _dialects = string.Join(", ", _dataSourceNames.Select(n =>
                {
                    var ds = db.GetDataSource(n);
                    return ds == null ? n + " (未定義)" : n + " = " + DbSchemaReader.DialectName(ds.DataSourceType);
                }));
            }
            finally
            {
                db.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            return _dialects;
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            yield return AIFunctionFactory.Create(
                ([Description("列を知りたい表の名前 (複数可)。省略すると表名の目次だけを返す。")] string[]? tables = null,
                 [Description("データソース名。省略すると全データソース。")] string? dataSource = null)
                    => GetSchemaAsync(tables, dataSource, context),
                "get_schema",
                "DB の表と列を返す。引数なし = 表名の目次 (表ごとの列数とモジュール名)。tables を指定 = その表の列 (型と、分かる範囲で業務上の名前・候補値・リンク)。");
            yield return AIFunctionFactory.Create(
                ([Description("この DB の方言で書いた、読み取り専用の SELECT 文 1 つ。")] string sql,
                 [Description("このクエリで何を調べるかを一文で (ユーザーに進捗として表示される)。")] string purpose,
                 [Description("実行するデータソース名 (get_schema の見出しにある名前)。データソースが 1 つだけなら省略可。")] string? dataSource = null)
                    => ExecuteSqlAsync(sql, purpose, dataSource, context),
                "execute_sql",
                "指定したデータソースで SELECT 文を 1 つ実行し、列と行を JSON で返す。上限を超えた行は切り捨てられる (truncated=true)。");
        }

        async Task<string> GetSchemaAsync(string[]? tables, string? dataSource, AIChatToolContext context)
        {
            context.Progress.Report(Resources.AIChat_ReadingSchema);
            var wanted = (tables ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            var sources = _dataSourceNames;
            if (!string.IsNullOrWhiteSpace(dataSource))
            {
                var found = _dataSourceNames.FirstOrDefault(n => string.Equals(n, dataSource.Trim(), StringComparison.OrdinalIgnoreCase));
                if (found == null) return $"データソース '{dataSource}' は使えません。使えるのは {string.Join(", ", _dataSourceNames)} です。";
                sources = new List<string> { found };
            }

            var schema = await LoadSchemaAsync(context.CancellationToken);
            var design = _design?.Invoke();
            var excluded = new HashSet<string>(_options.ExcludedTables, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            var matched = 0;
            foreach (var name in sources)
            {
                var byTable = schema[name].Where(c => !excluded.Contains(c.Table) && !excluded.Contains(TableNameOnly(c.Table))).GroupBy(c => c.Table).ToList();
                var modules = ModuleInfoByTable(design, name);
                sb.Append("## データソース ").Append(name).Append(" (").Append(Dialects().Split(", ").FirstOrDefault(d => d.StartsWith(name + " = ", StringComparison.OrdinalIgnoreCase))?[(name.Length + 3)..] ?? "").AppendLine(")");
                if (wanted.Count == 0)
                {
                    //目次: 表名 (列数) [モジュール]
                    foreach (var table in byTable)
                    {
                        sb.Append("- ").Append(table.Key).Append(" (").Append(table.Count()).Append(" 列)");
                        if (modules.TryGetValue(TableNameOnly(table.Key), out var m)) sb.Append(" [モジュール ").Append(m.ModuleName).Append(']');
                        sb.AppendLine();
                    }
                    if (byTable.Count == 0) sb.AppendLine("(この DB ユーザーから見える表はありません)");
                    matched += byTable.Count;
                }
                else
                {
                    //指定された表だけ、1 表 1 行で: table(col TYPE [説明], ...)
                    foreach (var table in byTable.Where(t => wanted.Any(w => string.Equals(w, t.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(w, TableNameOnly(t.Key), StringComparison.OrdinalIgnoreCase))))
                    {
                        var info = modules.TryGetValue(TableNameOnly(table.Key), out var m) ? m : null;
                        sb.Append("- ").Append(table.Key);
                        if (info != null) sb.Append(" [モジュール ").Append(info.ModuleName).Append(']');
                        sb.Append('(');
                        sb.Append(string.Join(", ", table.Select(c =>
                            c.Name + " " + c.DataType + (info != null && info.Columns.TryGetValue(c.Name, out var f) ? " [" + f + "]" : ""))));
                        sb.AppendLine(")");
                        matched++;
                    }
                }
            }
            if (wanted.Count > 0 && matched == 0) sb.AppendLine($"指定の表 ({string.Join(", ", wanted)}) は見つかりません。引数なしの get_schema で表名を確かめてください。");
            context.Logger?.LogInformation("AIChat RawDataAccess schema by {User}: tables={Tables} matched={Matched}", context.Request.UserName, wanted.Count == 0 ? "(index)" : string.Join(",", wanted), matched);
            return sb.ToString();
        }

        //全データソースの列情報 (10 分キャッシュ)。DB へ行くのはここだけ
        async Task<Dictionary<string, List<DbSchemaReader.Column>>> LoadSchemaAsync(CancellationToken cancellationToken)
        {
            lock (_schemaLock)
            {
                if (_schemaCache != null && DateTime.UtcNow - _schemaLoaded < _options.SchemaCacheDuration) return _schemaCache;
            }
            var result = new Dictionary<string, List<DbSchemaReader.Column>>(StringComparer.OrdinalIgnoreCase);
            await using var db = _dbAccessorFactory();
            foreach (var name in _dataSourceNames)
            {
                if (db.GetDataSource(name) == null) throw new InvalidOperationException($"Data source '{name}' is not defined.");
                result[name] = await DbSchemaReader.ReadAsync(db, name, _options.CommandTimeoutSeconds, cancellationToken);
            }
            lock (_schemaLock)
            {
                _schemaCache = result;
                _schemaLoaded = DateTime.UtcNow;
            }
            return result;
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

        //表名 → モジュール情報。設計上のデータソース名が AI 用データソース名と一致するモジュールを優先し、
        //一致するものが 1 つも無ければ (AI 用に別名の読み取り専用接続を作っている場合) 全モジュールから表名で結ぶ
        static Dictionary<string, ModuleInfo> ModuleInfoByTable(DesignData? design, string dataSourceName)
        {
            var result = new Dictionary<string, ModuleInfo>(StringComparer.OrdinalIgnoreCase);
            if (design == null) return result;
            var all = design.Modules.GetModuleNames().Select(design.Modules.Find).Where(m => m != null && !string.IsNullOrEmpty(m.DbTable)).Select(m => m!).ToList();
            var matched = all.Where(m => string.Equals(m.DataSourceName, dataSourceName, StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var module in matched.Count > 0 ? matched : all)
            {
                if (result.ContainsKey(module.DbTable)) continue;
                var info = new ModuleInfo { ModuleName = module.Name };
                foreach (var field in module.Fields)
                {
                    var column = (field as DbValueFieldDesignBase)?.DbColumn;
                    if (string.IsNullOrEmpty(column)) continue;
                    var description = field.Name;
                    if (field is ValueFieldDesignBase v && !string.IsNullOrEmpty(v.DisplayName) && v.DisplayName != field.Name) description += " (" + v.DisplayName + ")";
                    var candidates = DesignKnowledge.DesignDescriber.Candidates(design, field);
                    if (candidates.Count > 0) description += "; 候補値: " + string.Join(", ", candidates);
                    if (field is LinkFieldDesign link && !string.IsNullOrEmpty(link.SearchCondition.ModuleName))
                        description += $"; リンク → {link.SearchCondition.ModuleName}.{(string.IsNullOrEmpty(link.ValueVariable) ? SystemFieldNames.Id : link.ValueVariable)}";
                    info.Columns[column] = description;
                }
                result[module.DbTable] = info;
            }
            return result;
        }

        async Task<string> ExecuteSqlAsync(string sql, string purpose, string? dataSource, AIChatToolContext context)
        {
            var dataSourceName = ResolveDataSource(dataSource, out var dataSourceError);
            if (dataSourceName == null) return JsonSerializer.Serialize(new { error = dataSourceError });

            sql = (sql ?? string.Empty).Trim().TrimEnd(';').Trim();
            var rejection = Validate(sql);
            if (rejection != null)
            {
                context.Logger?.LogWarning("AIChat RawDataAccess rejected SQL by {User}: {Reason} / {Sql}", context.Request.UserName, rejection, sql);
                return JsonSerializer.Serialize(new { error = rejection });
            }

            context.Progress.Report(string.IsNullOrWhiteSpace(purpose) ? Resources.AIChat_RunningQuery : purpose.Trim());
            context.Logger?.LogInformation("AIChat RawDataAccess SQL by {User} (conversation {Conversation}) on {DataSource}: {Sql}", context.Request.UserName, context.Request.ConversationId, dataSourceName, sql);

            try
            {
                await using var db = _dbAccessorFactory();
                var connection = db.GetConnection(dataSourceName);
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.CommandTimeout = _options.CommandTimeoutSeconds;
                command.Transaction = db.GetTransaction(dataSourceName) as DbTransaction;
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
                return Serialize(dataSourceName, columns, rows, truncated);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                context.Logger?.LogWarning("AIChat RawDataAccess SQL failed: {Message}", e.Message);
                return JsonSerializer.Serialize(new { error = e.Message });
            }
        }

        string? ResolveDataSource(string? requested, out string? error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(requested))
            {
                if (_dataSourceNames.Count == 1) return _dataSourceNames[0];
                error = $"dataSource を指定してください。使えるのは {string.Join(", ", _dataSourceNames)} です。";
                return null;
            }
            var found = _dataSourceNames.FirstOrDefault(n => string.Equals(n, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            error = $"データソース '{requested}' は使えません。使えるのは {string.Join(", ", _dataSourceNames)} です。";
            return null;
        }

        string Serialize(string dataSource, List<string> columns, List<object?[]> rows, bool truncated)
        {
            while (true)
            {
                var json = JsonSerializer.Serialize(new { dataSource, columns, rowCount = rows.Count, truncated, rows });
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
