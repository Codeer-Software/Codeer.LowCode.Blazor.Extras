using System.Text.RegularExpressions;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
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
    /// 承認フローのセットアップ。承認データモジュール群 (フロー / メンバー / 履歴 + 任意の経路マスタ) を
    /// テンプレートから生成し、申請書モジュールへ結線する。経路マスタは契約を持たないただのモジュールで、
    /// 読み込み処理 (経路モジュールの .mod.cs の Load) と申請書スクリプトの雛形 (OnBuildRoute) を「出発点」として生成する。
    /// - 冪等: 同名モジュールが既に存在すれば生成せず結線だけを行う (使いまわし)。
    /// - 生成後は通常のモジュール (フィールド追加・画面カスタム・リネームすべて自由。契約フィールドが正)。
    /// - DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)。
    /// - メールを使う (UseTurnNotifyMail) ときは MailSetupService も呼び、差出人契約・送信履歴・サーバー設定の案内まで揃える。
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

        public static SetupResult Run(DesignData designData, string designDir, ApprovalSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            //ユーザーモジュールに差出人契約があれば、メールアドレス・表示名はその宣言に従う (二重に聞かない)
            var (contractEmail, contractDisplayName) = MailSetupService.ReadSenderRoles(designData.Modules.Find(options.UserModuleName));
            options.UserEmailField = contractEmail ?? options.UserEmailField;
            options.UserDisplayNameField = contractDisplayName ?? options.UserDisplayNameField;

            var templates = SelectTemplates(options.RouteMaster);
            var nameMap = templates.ToDictionary(t => t.BaseName, t => options.Prefix + t.BaseName);
            if (options.UserModuleName != ModuleTemplateEngine.TemplateUserModule)
                nameMap[ModuleTemplateEngine.TemplateUserModule] = options.UserModuleName;

            var tablePrefix = string.IsNullOrEmpty(options.Prefix)
                ? string.Empty
                : MailHistoryModuleFactory.ToSnakeCase(options.Prefix) + "_";

            //検索用モジュールの SQL が結合するユーザーテーブル (テーブル名・表示名列はユーザーモジュールのデザインから)
            var userModule = designData.Modules.Find(options.UserModuleName);
            var userTable = string.IsNullOrEmpty(userModule?.DbTable) ? "app_users" : userModule!.DbTable;
            var userNameColumn = (userModule?.Fields.FirstOrDefault(e => e.Name == options.UserDisplayNameField)
                as DbValueFieldDesignBase)?.DbColumn;
            if (string.IsNullOrEmpty(userNameColumn)) userNameColumn = "name";

            //検索用モジュールの「申請種別」= 申請書モジュール名を enum (メンバー名 = モジュール名 / 表示 = 申請書の表示名) で見せる。
            //結線先の申請書を最初のメンバーにして生成する (冪等: 既存はそのまま。申請書を増やしたらユーザーがメンバーを足す)
            var requestTypeEnumName = options.Prefix + RequestTypeEnumBaseName;
            EnsureRequestTypeEnum(designData, designDir, requestTypeEnumName, options.TargetModuleName, result);

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
                    moduleName, template.IsQuery ? string.Empty : tablePrefix + template.DbTable, options.DataSourceName, nameMap,
                    options.UserDisplayNameField, options.UserEmailField,
                    removeTurnNotifyMail: !options.UseTurnNotifyMail);

                //検索用モジュールの「申請種別」(TargetModuleName の Select) は申請種別 enum を参照する
                if (template.IsQuery)
                    json = ModuleTemplateEngine.SetSelectEnum(json, "TargetModuleName", requestTypeEnumName);

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
                    //SQL はテーブル名 (プレフィックス) とユーザーテーブルを差し替え、DB の方言に合わせる。テーブルは作らない
                    var sql = RewriteQuerySql(SetupTemplates.Load($"{template.BaseName}.Query.sql"),
                        dataSourceType, tablePrefix, userTable, userNameColumn);
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

            //申請書モジュールへの結線
            if (!string.IsNullOrEmpty(options.TargetModuleName))
            {
                WireParent(designData, designDir, options, nameMap, dataSourceType, existingTables, result);
            }

            //メールを使うなら、通知メールが動く前提 (差出人契約・送信履歴・サーバー設定) も同時に揃える
            if (options.UseTurnNotifyMail)
            {
                result.Merge(MailSetupService.Run(designData, designDir, new MailSetupOptions
                {
                    UserModuleName = options.UserModuleName,
                    UserEmailField = options.UserEmailField,
                    UserDisplayNameField = options.UserDisplayNameField,
                    AddSenderContract = true,
                    AddGmailTokenField = false,
                    CreateHistoryModule = options.UseMailHistory,
                    DataSourceName = options.DataSourceName,
                    AddPageFrameLink = options.AddPageFrameLinks,
                }, dataSourceType, existingTables));
            }

            //PageFrame へのページリンク追加 (生成したモジュールのみ)
            if (options.AddPageFrameLinks)
            {
                //検索用モジュールは「一覧だけ + 開くで申請書へ」(詳細遷移・作成・削除なし)。並びは SQL の ORDER BY
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

        static void WireParent(DesignData designData, string designDir, ApprovalSetupOptions options,
            Dictionary<string, string> nameMap, DataSourceType dataSourceType,
            List<DbTableDefinition>? existingTables, SetupResult result)
        {
            var parent = designData.Modules.Find(options.TargetModuleName);
            if (parent == null)
            {
                result.Notes.Add($"申請書モジュール {options.TargetModuleName} が見つからないため、結線をスキップしました。");
                return;
            }
            if (parent.Fields.OfType<ApprovalFlowFieldDesign>().Any())
            {
                result.Notes.Add($"{options.TargetModuleName} には既に ApprovalFlowField があるため、結線をスキップしました。");
                return;
            }

            var field = new ApprovalFlowFieldDesign
            {
                Name = options.FieldName,
                DbColumn = options.DbColumn,
                FlowModuleName = nameMap[Flow.BaseName],
                OnBuildRoute = "OnBuildRoute",
            };
            parent.Fields.Add(field);

            //編集ロックの既定条件 (未申請 or 編集して再申請できる状態)。既存の条件は壊さない
            if (parent.DataWriteCondition.Condition == null && string.IsNullOrEmpty(parent.DataWriteCondition.ModuleName))
            {
                parent.DataWriteCondition = CreateDefaultDataWriteCondition(parent.Name, options.FieldName);
            }
            else
            {
                result.Notes.Add($"{parent.Name} には既に DataWriteCondition があるため、編集ロック条件は設定しませんでした。必要なら「{options.FieldName}.Status が 未申請/差し戻し/取り下げ/却下」の条件を組み込んでください。");
            }

            //権限のクライアント評価用に、条件で使うフロー状態をロードデータへ同梱する
            var statusPath = $"{options.FieldName}.Status";
            foreach (var layout in parent.DetailLayouts.Values)
            {
                if (!layout.DataOnlyFields.Contains(statusPath)) layout.DataOnlyFields.Add(statusPath);
            }
            if (!parent.LinkFieldNames.Contains(statusPath)) parent.LinkFieldNames.Add(statusPath);

            //既定詳細レイアウトの末尾にフィールドを配置する (Grid のときのみ)
            if (parent.DetailLayouts.TryGetValue(string.Empty, out var detail) && detail.Layout is GridLayoutDesign grid)
            {
                grid.Rows.Add(new GridRow
                {
                    Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = options.FieldName } } }
                });
            }
            else
            {
                result.Notes.Add($"{parent.Name} の既定詳細レイアウトが Grid ではないため、{options.FieldName} の配置はスキップしました。レイアウトに手動で配置してください。");
            }

            //経路組み立てスクリプトの雛形 (無ければ追記)
            designData.Scripts.TryGetValue(parent.Name, out var script);
            script ??= LoadScriptFile(designDir, parent.Name) ?? string.Empty;
            if (!script.Contains("OnBuildRoute"))
            {
                var stub = CreateOnBuildRouteStub(options.FieldName, options.RouteMaster, nameMap);
                script = string.IsNullOrWhiteSpace(script) ? stub : script.TrimEnd() + "\r\n\r\n" + stub;
                SaveDesignFile(designDir, "Modules", $"{parent.Name}.mod.cs", script);
                designData.Scripts[parent.Name] = script;
            }

            //dotted リンク列 ("Approval.Status" 等) はロード時にフィールドへ自動合成される。
            //ファイルには合成前の形 (LinkFieldNames のみ) を書くため、合成済みクローンを取り除いて保存する
            var toSave = parent.JsonClone();
            toSave.Fields.RemoveAll(f => f.Name.Contains('.'));
            SaveDesignFile(designDir, "Modules", $"{parent.Name}.mod.json", JsonConverterEx.SerializeObject(toSave));
            result.ParentWired = true;
            result.Ddl.AddRange(SetupDbMapping.CreateAlterAddForField(parent, field, dataSourceType, existingTables));
        }

        internal static ModuleMatchCondition CreateDefaultDataWriteCondition(string moduleName, string fieldName)
        {
            //汎用条件エディタの行モデル (Or 直下に葉条件): 未申請 (フロー行なし = null) + 編集して再申請できる3状態
            var condition = new MultiMatchCondition { IsOrMatch = true };
            condition.Children.Add(new FieldValueMatchCondition
            {
                SearchTargetVariable = $"{fieldName}.Status.Value",
                Comparison = MatchComparison.Equal,
                Value = new NullValue(),
            });
            foreach (var status in new[] { "Returned", "Withdrawn", "Rejected" })
            {
                condition.Children.Add(new FieldValueMatchConditionNonNull
                {
                    SearchTargetVariable = $"{fieldName}.Status.Value",
                    Comparison = MatchComparison.Equal,
                    Value = new StringValue { Value = status },
                });
            }
            return new ModuleMatchCondition { ModuleName = moduleName, Condition = condition };
        }

        static string CreateOnBuildRouteStub(string fieldName, ApprovalRouteMasterKind routeMaster,
            Dictionary<string, string> nameMap)
            => routeMaster == ApprovalRouteMasterKind.None
                ? $$"""
                    // 承認経路を組み立てる ({{fieldName}} の「経路組み立て」に設定済み。null を返すと申請中止)
                    ApprovalRouteData OnBuildRoute()
                    {
                        // スクリプトで経路を組み立てるサンプル。実際の承認者の決め方に書き換えてください
                        var route = {{fieldName}}.NewRoute("標準経路");
                        var step = route.AddStep("上長承認");
                        step.AddMember(CurrentUser.Id, true);  // TODO: 承認者のユーザーIdを設定する
                        return route;
                    }
                    """
                : $$"""
                    // 承認経路を組み立てる ({{fieldName}} の「経路組み立て」に設定済み。null を返すと申請中止)。
                    // 経路マスタ (経路 / ステップ / ステップ承認者) はただのモジュールで、承認フロー側はこのスクリプトが返す
                    // 経路しか見ない。マスタの読み込みと検証 (経路が無い / 自己承認) は経路マスタモジュール
                    // ({{nameMap[Route.BaseName]}}.mod.cs) の Load に共通化してあり、承認者の決め方 (役職・部署など) を
                    // 変えたいときはそちらを書き換える。経路マスタ画面で経路を作成し、その経路名に合わせてください
                    ApprovalRouteData OnBuildRoute()
                    {
                        return new {{nameMap[Route.BaseName]}}().Load("標準経路");
                    }
                    """;

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
        /// テーブル名のプレフィックス、ユーザーテーブル (app_users / u.name)、名前の連結 (GROUP_CONCAT)、
        /// パラメータ接頭辞 (Oracle は :)。
        /// </summary>
        internal static string RewriteQuerySql(string sql, DataSourceType dataSourceType, string tablePrefix,
            string userTable, string userNameColumn)
        {
            sql = Regex.Replace(sql, @"\bapproval_flow_members\b", tablePrefix + "approval_flow_members");
            sql = Regex.Replace(sql, @"\bapproval_flows\b", tablePrefix + "approval_flows");
            sql = Regex.Replace(sql, @"\bapproval_histories\b", tablePrefix + "approval_histories");
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

        static void ConfigureReadOnlyList(PageLink link, MatchConditionBase? condition)
        {
            link.ListPageDesign.UseNavigateToCreate = false;
            if (link.ListPageDesign.ListFieldDesign is not ListFieldDesign list) return;
            list.CanNavigateToDetail = false;
            list.CanCreate = false;
            list.CanUpdate = false;
            list.CanDelete = false;
            list.SearchCondition.SortConditions = [new SortCondition { Variable = "Id.Value", IsDescending = true }];
            list.SearchCondition.Condition = condition;
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

        internal static string? LoadScriptFile(string designDir, string moduleName)
        {
            var path = Path.Combine(designDir, "Modules", $"{moduleName}.mod.cs");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        /// <summary>申請種別 enum の既定名 (プレフィックスが前に付く)。</summary>
        internal const string RequestTypeEnumBaseName = "ApprovalRequestType";

        //申請種別 enum を生成する (既存なら触らない)。メンバー = 申請書モジュール名 (表示 = 申請書の表示名)
        static void EnsureRequestTypeEnum(DesignData designData, string designDir, string enumName,
            string targetModuleName, SetupResult result)
        {
            var path = Path.Combine(designDir, "Enums", $"{enumName}.enum.json");
            if (designData.Enums.Any(e => e.Name == enumName) || File.Exists(path)) return;

            var enumDesign = new EnumDesign { Name = enumName, ValueType = EnumValueType.String };
            var target = string.IsNullOrEmpty(targetModuleName) ? null : designData.Modules.Find(targetModuleName);
            if (target != null)
            {
                enumDesign.Members.Add(new EnumMemberDesign
                {
                    Name = target.Name,
                    DisplayText = string.IsNullOrEmpty(target.PageTitle) ? target.Name : target.PageTitle,
                });
            }
            designData.Enums.Add(enumDesign);
            SaveDesignFile(designDir, "Enums", $"{enumName}.enum.json", JsonConverterEx.SerializeObject(enumDesign));
            result.Notes.Add($"申請種別 enum {enumName} を生成しました。承認する申請書モジュールを増やしたら、メンバー (名前 = モジュール名) を追加してください。");
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
