using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// 監査ログのセットアップ。監査ログのテーブル (Extras.Server の DatabaseAuditSink が書く固定列) を閲覧するモジュールを
    /// テンプレート (Example の AuditLog) から生成し、テーブル作成 DDL (日時のインデックス込み) を返す。それだけ。
    /// 記録自体はサーバー機能 (appsettings の AuditLog で有効化) で、このモジュールは閲覧用。追加・更新・削除はサーバーが拒否する。
    /// - 冪等: 同名モジュールが既に存在すれば生成しない。テーブルが既にあれば DDL は出さない
    /// - 生成後は通常のモジュール (閲覧条件・レイアウトの調整は自由。列名は固定なので DbColumn は変えない)
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)
    /// </summary>
    public static class AuditLogSetupService
    {
        const string TemplateModuleName = "AuditLog";

        public static SetupResult Run(DesignData designData, string designDir, AuditLogSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();
            var moduleName = options.ModuleName;

            if (ModuleExists(designData, designDir, moduleName))
            {
                result.SkippedModules.Add(moduleName);
            }
            else
            {
                var nameMap = new Dictionary<string, string> { [TemplateModuleName] = moduleName };
                if (options.UserModuleName != ModuleTemplateEngine.TemplateUserModule)
                    nameMap[ModuleTemplateEngine.TemplateUserModule] = options.UserModuleName;

                var json = ModuleTemplateEngine.RewriteModuleJson(
                    SetupTemplates.Load($"{TemplateModuleName}.mod.json"),
                    moduleName, options.TableName, options.DataSourceName, nameMap, options.UserDisplayNameField);

                //型付きで読み直して正規化する (プロパティ名・型の崩れをここで検出し、デザイナ保存と同じ形で書き出す)
                var module = JsonConverterEx.DeserializeObject<ModuleDesign>(json)
                    ?? throw new InvalidOperationException($"Broken template: {TemplateModuleName}");
                ApprovalFlowSetupService.SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.json", JsonConverterEx.SerializeObject(module));
                result.CreatedModules.Add(moduleName);

                if (options.AddPageFrameLink)
                {
                    ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir,
                        new List<(string, string, Action<PageLink>?)> { ("監査ログ", moduleName, ConfigureList) }, result);
                }
            }

            //テーブルの列はサーバー (DatabaseAuditSink) が決めるので、モジュールからではなくその DDL を出す
            if (existingTables?.Any(t => string.Equals(t.Name, options.TableName, StringComparison.OrdinalIgnoreCase)) == true)
                result.Notes.Add($"テーブル {options.TableName} は既にあるので DDL は出していません。");
            else
                result.Ddl.AddRange(AuditLogTableDdl.Create(dataSourceType, options.TableName));

            result.Notes.Add(CreateNextStepsNote(options));
            return result;
        }

        static bool ModuleExists(DesignData designData, string designDir, string moduleName)
            => designData.Modules.Find(moduleName) != null
                || File.Exists(Path.Combine(designDir, "Modules", $"{moduleName}.mod.json"));

        //監査ログ一覧のページ: 新規作成・編集・削除なし (追記専用の記録)。行の「>」で 1 件の詳細 (対象・補足の全文) へ
        static void ConfigureList(PageLink link)
        {
            link.ListPageDesign.UseNavigateToCreate = false;
            if (link.ListPageDesign.ListFieldDesign is not ListFieldDesign list) return;
            list.CanNavigateToDetail = true;
            list.CanCreate = false;
            list.CanUpdate = false;
            list.CanDelete = false;
            list.SearchCondition.SortConditions = [new SortCondition { Variable = "Id.Value", IsDescending = true }];
        }

        //以降の手順 (記録の有効化はホストの appsettings。閲覧の制限と追記専用の担保は運用)
        static string CreateNextStepsNote(AuditLogSetupOptions options)
            => $"""
                監査ログの閲覧モジュールを生成しました。記録はサーバー機能なので、次の手順で仕上げてください:
                1. ホストの appsettings で有効にする: AuditLog.Enabled = true、AuditLogDatabase.DataSourceName = {options.DataSourceName}、Table = {options.TableName} (このモジュールと同じテーブル)。
                   結線はアプリテンプレート (Cookie) に含まれています (docs/AuditLog.md「ホストの結線」)
                2. 閲覧できる人を絞る: {options.ModuleName} の UserReadCondition を監査役・管理者に設定する (生成直後は誰でも読めます)
                3. 追記専用を担保する: 監査ログのデータソースは INSERT (閲覧用に SELECT) だけの DB ユーザーで繋ぎ、掃除をアプリに任せない (RetentionDays = 0) 構成が厳密です (docs/AuditLog.md「改ざん対策」)
                このモジュールからの追加・更新・削除はサーバー (AuditIOInterceptor) が拒否します。列名は固定なので DbColumn は変えないでください。
                """;
    }

    /// <summary>
    /// 監査ログのテーブルの DDL。Extras.Server の <c>DatabaseAuditSink.CreateTableSql</c> / <c>CreateIndexSql</c> と同一ロジックの複製
    /// (Extras.Designer は Extras.Server を参照しない方針のため。同一であることはテストで確認する。挙動を変えるときは両方を直す)。
    /// </summary>
    internal static class AuditLogTableDdl
    {
        /// <summary>テーブル作成と日時のインデックス (2 文)。</summary>
        internal static List<string> Create(DataSourceType type, string table)
        {
            var ddl = CreateTableSql(type, table).Split('\n').Select(e => e.TrimEnd('\r')).ToList();
            ddl[^1] += ";";
            ddl.Add(CreateIndexSql(type, table) + ";");
            return ddl;
        }

        internal static string CreateTableSql(DataSourceType type, string table)
        {
            var d = new Dialect(type);
            string id = type switch
            {
                DataSourceType.SQLServer => "bigint identity(1,1) primary key",
                DataSourceType.PostgreSQL => "bigserial primary key",
                DataSourceType.MySQL => "bigint auto_increment primary key",
                DataSourceType.Oracle => "number generated always as identity primary key",
                _ => "integer primary key autoincrement",
            };
            string dateTime = type switch
            {
                DataSourceType.SQLServer => "datetime2",
                DataSourceType.PostgreSQL => "timestamp",
                DataSourceType.MySQL => "datetime(3)",
                DataSourceType.Oracle => "timestamp",
                _ => "text",
            };
            string Varchar(int n) => type switch
            {
                DataSourceType.SQLServer => $"nvarchar({n})",
                DataSourceType.Oracle => $"varchar2({n} char)",
                DataSourceType.SQLite => "text",
                _ => $"varchar({n})",
            };
            string text = type switch
            {
                DataSourceType.SQLServer => "nvarchar(max)",
                DataSourceType.MySQL => "longtext",
                DataSourceType.Oracle => "clob",
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

        internal static string CreateIndexSql(DataSourceType type, string table)
        {
            var d = new Dialect(type);
            return $"create index {d.Quote($"ix_{table}_occurred_at")} on {d.Quote(table)} ({d.Quote("occurred_at_utc")})";
        }

        readonly struct Dialect
        {
            readonly DataSourceType _type;
            public Dialect(DataSourceType type) => _type = type;
            public string Quote(string name) => _type switch
            {
                DataSourceType.SQLServer => $"[{name}]",
                DataSourceType.MySQL => $"`{name}`",
                _ => $"\"{name}\"",
            };
        }
    }
}
