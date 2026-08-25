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
    /// </summary>
    public static class ApprovalFlowSetupService
    {
        record TemplateInfo(string BaseName, string DbTable, bool IsRouteMaster);

        static readonly TemplateInfo Flow = new("ApprovalFlow", "approval_flows", false);
        static readonly TemplateInfo Member = new("ApprovalFlowMember", "approval_flow_members", false);
        static readonly TemplateInfo History = new("ApprovalHistory", "approval_histories", false);
        static readonly TemplateInfo Route = new("ApprovalRoute", "approval_routes", true);
        static readonly TemplateInfo RouteStep = new("ApprovalRouteStep", "approval_route_steps", true);
        static readonly TemplateInfo RouteStepMember = new("ApprovalRouteStepMember", "approval_route_step_members", true);

        public static SetupResult Run(DesignData designData, string designDir, ApprovalSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            var templates = SelectTemplates(options.RouteMaster);
            var nameMap = templates.ToDictionary(t => t.BaseName, t => options.Prefix + t.BaseName);
            if (options.UserModuleName != ModuleTemplateEngine.TemplateUserModule)
                nameMap[ModuleTemplateEngine.TemplateUserModule] = options.UserModuleName;

            var tablePrefix = string.IsNullOrEmpty(options.Prefix)
                ? string.Empty
                : MailHistoryModuleFactory.ToSnakeCase(options.Prefix) + "_";

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
                    moduleName, tablePrefix + template.DbTable, options.DataSourceName, nameMap,
                    options.UserDisplayNameField, options.UserEmailField,
                    removeTurnNotifyMail: !options.UseTurnNotifyMail);

                //型付きで読み直して正規化する (プロパティ名・型の崩れをここで検出し、デザイナ保存と同じ形で書き出す)
                var module = JsonConverterEx.DeserializeObject<ModuleDesign>(json)
                    ?? throw new InvalidOperationException($"Broken template: {template.BaseName}");

                SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.json", JsonConverterEx.SerializeObject(module));

                if (template == Member || template == Route)
                {
                    var script = ModuleTemplateEngine.RewriteScript(
                        SetupTemplates.Load($"{template.BaseName}.mod.cs"), nameMap);
                    SaveDesignFile(designDir, "Modules", $"{moduleName}.mod.cs", script);
                    designData.Scripts[moduleName] = script;
                }

                result.CreatedModules.Add(moduleName);
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

            //PageFrame へのページリンク追加 (生成したモジュールのみ)
            if (options.AddPageFrameLinks)
            {
                var links = new List<(string Title, string Module)>
                {
                    ("承認待ち一覧", nameMap[Member.BaseName]),
                    ("承認フロー管理", nameMap[Flow.BaseName]),
                };
                if (options.RouteMaster != ApprovalRouteMasterKind.None)
                    links.Add(("承認経路マスタ", nameMap[Route.BaseName]));

                AddPageFrameLinks(designData, designDir,
                    links.Where(e => result.CreatedModules.Contains(e.Module)).ToList(), result);
            }

            return result;
        }

        static List<TemplateInfo> SelectTemplates(ApprovalRouteMasterKind routeMaster)
        {
            var templates = new List<TemplateInfo> { Flow, Member, History };
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

        internal static void AddPageFrameLinks(DesignData designData, string designDir,
            List<(string Title, string Module)> links, SetupResult result)
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
            foreach (var (title, module) in links)
            {
                if (frame.Left.Links.Any(e => e.Module == module)) continue;
                frame.Left.Links.Add(new PageLink { Title = title, Module = module });
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

        internal static void SaveDesignFile(string designDir, string subDir, string fileName, string content)
        {
            var dir = Path.Combine(designDir, subDir);
            Directory.CreateDirectory(dir);
            //デザイナの保存と同じ BOM 付き UTF-8
            File.WriteAllText(Path.Combine(dir, fileName), content, new UTF8Encoding(true));
        }
    }
}
