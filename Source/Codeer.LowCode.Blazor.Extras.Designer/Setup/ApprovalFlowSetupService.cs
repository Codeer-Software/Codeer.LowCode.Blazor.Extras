using System.Text.RegularExpressions;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// 承認フローのセットアップ。承認モジュール群 (フロー / メンバー / 履歴 + 検索用 2 つ + 任意の経路マスタ) を
    /// テンプレートから生成する。それだけ。申請書側 (ApprovalFlowField の配置・OnBuildRoute) はユーザーがデザイナで行う。
    /// 経路マスタは契約を持たないただのモジュールで、読み込み処理 (経路モジュールの .mod.cs の Load) を「出発点」として生成する。
    /// - 冪等: 同名モジュールが既に存在すれば生成しない (承認モジュール群は 1 セット。申請書が増えても同じセットを共有する)。
    /// - 生成後は通常のモジュール (フィールド追加・画面カスタム・リネームすべて自由。契約フィールドが正)。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)。
    /// - 通知メールを含める場合、メール側の準備 (差出人契約・送信履歴・サーバー設定) は先にメールのセットアップで済ませておく。
    /// </summary>
    public static class ApprovalFlowSetupService
    {
        record TemplateInfo(string BaseName, string DbTable, bool IsRouteMaster, bool IsQuery = false);

        //エンジン用 (承認フローが読み書きする。UI は持たない)
        static readonly TemplateInfo Flow = new("ApprovalFlow", "approval_flows", false);
        static readonly TemplateInfo Member = new("ApprovalFlowMember", "approval_flow_members", false);
        static readonly TemplateInfo History = new("ApprovalHistory", "approval_histories", false);
        //検索用 (QueryField。テーブルを持たず SQL で承認テーブルを読む。一覧と「開く」だけ)
        static readonly TemplateInfo MyList = new("MyApprovalList", "", false, IsQuery: true);
        static readonly TemplateInfo StatusList = new("ApprovalStatusList", "", false, IsQuery: true);
        static readonly TemplateInfo Route = new("ApprovalRoute", "approval_routes", true);
        static readonly TemplateInfo RouteStep = new("ApprovalRouteStep", "approval_route_steps", true);
        static readonly TemplateInfo RouteStepMember = new("ApprovalRouteStepMember", "approval_route_step_members", true);

        /// <summary>申請種別 enum の名前 (検索用モジュールの「申請種別」。メンバー名 = 申請書モジュール名)。</summary>
        internal const string RequestTypeEnumName = "ApprovalRequestType";

        public static SetupResult Run(DesignData designData, string designDir, ApprovalSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            //ユーザーモジュールに差出人契約があれば、メールアドレス・表示名はその宣言に従う (二重に聞かない)
            var (contractEmail, contractDisplayName) = MailSetupService.ReadSenderRoles(designData.Modules.Find(options.UserModuleName));
            options.UserEmailField = contractEmail ?? options.UserEmailField;
            options.UserDisplayNameField = contractDisplayName ?? options.UserDisplayNameField;

            var templates = SelectTemplates(options.RouteMaster);
            //生成名 = テンプレート名 (承認モジュール群は 1 セット)
            var nameMap = templates.ToDictionary(t => t.BaseName, t => t.BaseName);
            if (options.UserModuleName != ModuleTemplateEngine.TemplateUserModule)
                nameMap[ModuleTemplateEngine.TemplateUserModule] = options.UserModuleName;

            //検索用モジュールの SQL が結合するユーザーテーブル (テーブル名・表示名列はユーザーモジュールのデザインから)
            var userModule = designData.Modules.Find(options.UserModuleName);
            var userTable = string.IsNullOrEmpty(userModule?.DbTable) ? "app_users" : userModule!.DbTable;
            var userNameColumn = (userModule?.Fields.FirstOrDefault(e => e.Name == options.UserDisplayNameField)
                as DbValueFieldDesignBase)?.DbColumn;
            if (string.IsNullOrEmpty(userNameColumn)) userNameColumn = "name";

            //検索用モジュールの「申請種別」= 申請書モジュール名の enum (メンバー名 = モジュール名 / 表示 = 申請書の表示名)
            EnsureRequestTypeEnum(designData, designDir, result);

            //モジュール生成 (冪等: 既存はスキップ)
            foreach (var template in templates)
            {
                var moduleName = nameMap[template.BaseName];
                if (ModuleExists(designData, designDir, moduleName))
                {
                    result.SkippedModules.Add(moduleName);
                    continue;
                }

                var json = ModuleTemplateEngine.RewriteModuleJson(
                    SetupTemplates.Load($"{template.BaseName}.mod.json"),
                    moduleName, template.IsQuery ? string.Empty : template.DbTable, options.DataSourceName, nameMap,
                    options.UserDisplayNameField, options.UserEmailField,
                    removeTurnNotifyMail: !options.UseTurnNotifyMail);

                //検索用モジュールの「申請種別」(TargetModuleName の Select) は申請種別 enum を参照する
                if (template.IsQuery)
                    json = ModuleTemplateEngine.SetSelectEnum(json, "TargetModuleName", RequestTypeEnumName);

                //型付きで読み直して正規化する (プロパティ名・型の崩れをここで検出し、デザイナ保存と同じ形で書き出す)
                var module = JsonConverterEx.DeserializeObject<ModuleDesign>(json)
                    ?? throw new InvalidOperationException($"Broken template: {template.BaseName}");

                SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.json", JsonConverterEx.SerializeObject(module));

                if (template == Route || template.IsQuery)
                {
                    var script = ModuleTemplateEngine.RewriteScript(
                        SetupTemplates.Load($"{template.BaseName}.mod.cs"), nameMap);
                    SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.cs", script);
                    designData.Scripts[moduleName] = script;
                }

                result.CreatedModules.Add(moduleName);
                if (template.IsQuery)
                {
                    //SQL はユーザーテーブルを差し替え、DB の方言に合わせる。テーブルは作らない
                    var sql = RewriteQuerySql(SetupTemplates.Load($"{template.BaseName}.Query.sql"),
                        dataSourceType, userTable, userNameColumn);
                    SaveDesignFile(designDir, "Modules", $"{moduleName}.Query.sql", sql);
                    continue;
                }
                result.Ddl.AddRange(module.CreateDDL(dataSourceType, existingTables));
            }

            if (options.RouteMaster != ApprovalRouteMasterKind.None
                && result.CreatedModules.Any(e => e == nameMap[Route.BaseName]))
            {
                result.Notes.Add("経路マスタは誰でも編集できる状態で生成されます。管理者だけが編集できるようにするには、経路マスタモジュールの UserWriteCondition に管理者条件を設定してください。");
            }

            //PageFrame へのページリンク追加 (生成したモジュールのみ)
            if (options.AddPageFrameLinks)
            {
                //検索用モジュールは「一覧だけ + 開くで申請書へ」(詳細遷移・作成・削除なし)
                var links = new List<(string Title, string Module, Action<PageLink>? Configure)>
                {
                    ("承認待ち", nameMap[MyList.BaseName], ConfigureQueryList),
                    ("承認状況", nameMap[StatusList.BaseName], ConfigureQueryList),
                };
                if (options.RouteMaster != ApprovalRouteMasterKind.None)
                    links.Add(("承認経路マスタ", nameMap[Route.BaseName], null));

                AddPageFrameLinks(designData, designDir,
                    links.Where(e => result.CreatedModules.Contains(e.Module)).ToList(), result);
            }

            //次にやること (申請書側はユーザーの作業)
            result.Notes.Add(CreateNextStepsNote(options, nameMap));
            if (options.UseTurnNotifyMail)
            {
                result.Notes.Add("通知メールを送るにはメール側の準備が必要です。まだなら Tools > メールのセットアップ (差出人契約・送信履歴・サーバー設定の案内) を実行してください。");
            }

            return result;
        }

        static List<TemplateInfo> SelectTemplates(ApprovalRouteMasterKind routeMaster)
        {
            var templates = new List<TemplateInfo> { Flow, Member, History, MyList, StatusList };
            if (routeMaster == ApprovalRouteMasterKind.Standard)
            {
                templates.Add(Route);
                templates.Add(RouteStep);
                templates.Add(RouteStepMember);
            }
            return templates;
        }

        static bool ModuleExists(DesignData designData, string designDir, string moduleName)
            => designData.Modules.Find(moduleName) != null
                || File.Exists(Path.Combine(designDir, "Modules", $"{moduleName}.mod.json"));

        //申請書側の手順 (セットアップはここまで。以降はデザイナで申請書モジュールに手を入れる)
        static string CreateNextStepsNote(ApprovalSetupOptions options, Dictionary<string, string> nameMap)
        {
            var route = options.RouteMaster == ApprovalRouteMasterKind.None
                ? $"    var route = new ApprovalRouteData(); route.AddStep(\"上長承認\").AddMember(CurrentUser.Id, true); return route;"
                : $"    return new {nameMap[Route.BaseName]}().Load(\"標準経路\");";
            return $$"""
                承認モジュール群を生成しました。申請書モジュール側は次の手順で仕上げてください:
                1. 申請書モジュールに ApprovalFlowField を置く (フローモジュール名 = {{nameMap[Flow.BaseName]}}、FK 列を DB に追加)
                2. 申請書のスクリプトに経路組み立てを書き、フィールドの「経路組み立て」に設定する
                   ApprovalRouteData OnBuildRoute()
                   {
                {{route}}
                   }
                3. 申請書の編集ロック: DataWriteCondition に「(フィールド名).Status が 未申請(null) / Returned / Withdrawn / Rejected」の Or 条件を設定
                4. 申請種別 enum {{RequestTypeEnumName}} にメンバー (名前 = 申請書モジュール名 / 表示 = 申請書の名前) を追加
                """;
        }

        //一覧だけのページ (新規作成・詳細遷移・削除なし。新しい順。条件は任意)
        static void ConfigureQueryList(PageLink link)
        {
            link.ListPageDesign.UseNavigateToCreate = false;
            if (link.ListPageDesign.ListFieldDesign is not ListFieldDesign list) return;
            list.CanNavigateToDetail = false;
            list.CanCreate = false;
            list.CanUpdate = false;
            list.CanDelete = false;
            //並びは申請日の降順 (QuerySortType = System で SQL の外から付く)
            list.SearchCondition.SortConditions = [new SortCondition { Variable = "SubmittedAt.Value", IsDescending = true }];
            list.SearchCondition.Condition = null;
        }

        /// <summary>
        /// 検索用モジュールのテンプレート SQL (Example = SQLite 文) を生成先に合わせて書き換える。
        /// ユーザーテーブル (app_users / u.name)、名前の連結 (GROUP_CONCAT)、パラメータ接頭辞 (Oracle は :)。
        /// </summary>
        internal static string RewriteQuerySql(string sql, DataSourceType dataSourceType,
            string userTable, string userNameColumn)
        {
            sql = Regex.Replace(sql, @"\bapp_users\b", userTable);
            sql = Regex.Replace(sql, @"\b(u2?)\.name\b", "$1." + userNameColumn);
            sql = Regex.Replace(sql, @"GROUP_CONCAT\((\w+\.\w+), '、'\)", m => dataSourceType switch
            {
                DataSourceType.PostgreSQL or DataSourceType.SQLServer => $"STRING_AGG({m.Groups[1].Value}, '、')",
                DataSourceType.MySQL => $"GROUP_CONCAT({m.Groups[1].Value} SEPARATOR '、')",
                DataSourceType.Oracle => $"LISTAGG({m.Groups[1].Value}, '、') WITHIN GROUP (ORDER BY {m.Groups[1].Value})",
                _ => m.Value,
            });
            if (dataSourceType == DataSourceType.Oracle) sql = Regex.Replace(sql, @"@(\w+)", ":$1");
            return sql;
        }

        internal static void AddPageFrameLinks(DesignData designData, string designDir,
            List<(string Title, string Module, Action<PageLink>? Configure)> links, SetupResult result)
        {
            if (links.Count == 0) return;

            var frame = designData.PageFrames.Find("Main")
                ?? designData.PageFrames.GetPageFrameNames().Select(designData.PageFrames.Find).FirstOrDefault();
            if (frame == null)
            {
                result.Notes.Add("PageFrame が無いため、ページリンクの追加をスキップしました。");
                return;
            }

            var added = false;
            foreach (var (title, module, configure) in links)
            {
                if (frame.Left.Links.Any(e => e.Module == module)) continue;
                var link = new PageLink { Title = title, Module = module };
                configure?.Invoke(link);
                frame.Left.Links.Add(link);
                added = true;
            }
            if (!added) return;

            SaveDesignFile(designDir, "PageFrames", $"{frame.Name}.frm.json", JsonConverterEx.SerializeObject(frame));
        }

        //申請種別 enum を生成する (既存なら触らない)。メンバーはユーザーが申請書ごとに足す (名前 = 申請書モジュール名)
        static void EnsureRequestTypeEnum(DesignData designData, string designDir, SetupResult result)
        {
            var path = Path.Combine(designDir, "Enums", $"{RequestTypeEnumName}.enum.json");
            if (designData.Enums.Any(e => e.Name == RequestTypeEnumName) || File.Exists(path)) return;

            var enumDesign = new EnumDesign { Name = RequestTypeEnumName, ValueType = EnumValueType.String };
            designData.Enums.Add(enumDesign);
            SaveDesignFile(designDir, "Enums", $"{RequestTypeEnumName}.enum.json", JsonConverterEx.SerializeObject(enumDesign));
        }

        internal static void SaveDesignFile(string designDir, string subDir, string fileName, string content)
        {
            var dir = Path.Combine(designDir, subDir);
            Directory.CreateDirectory(dir);
            //デザイナの保存と同じ BOM 付き UTF-8
            File.WriteAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(true));
        }
    }
}
