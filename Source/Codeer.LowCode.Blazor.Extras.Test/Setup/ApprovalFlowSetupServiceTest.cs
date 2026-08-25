using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class ApprovalFlowSetupServiceTest : SetupTestBase
    {
        static ApprovalSetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
            TargetModuleName = "Request",
        };

        [Test]
        public void 既定構成で承認モジュール一式が生成され契約が解決する()
        {
            CreateFixture();
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[]
            {
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory",
                "ApprovalRoute", "ApprovalRouteStep", "ApprovalRouteStepMember",
            }));
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalFlowMember.mod.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalFlow.mod.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalRoute.mod.cs")), Is.True);

            //実際の読込経路で読み直して構造を検証する
            var d = Load();
            var flow = d.Modules.Find("ApprovalFlow")!;
            Assert.That(flow, Is.Not.Null);
            Assert.That(flow.DataSourceName, Is.EqualTo("Main"));
            Assert.That(ApprovalContracts.Flow(flow), Is.Not.Null);

            //契約チェックが全部通ること (役割フィールド存在 + 一覧役割の連鎖)
            var dbDefs = new Dictionary<string, List<DbTableDefinition>>();
            Assert.That(flow.Fields.OfType<ApprovalFlowContractFieldDesign>().Single()
                .CheckDesign(new DesignCheckContext("ApprovalFlow", d, dbDefs)), Is.Empty);

            //申請書への結線
            var request = d.Modules.Find("Request")!;
            var field = request.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            Assert.That(field.Name, Is.EqualTo("Approval"));
            Assert.That(field.DbColumn, Is.EqualTo("approval_id"));
            Assert.That(field.FlowModuleName, Is.EqualTo("ApprovalFlow"));
            Assert.That(field.OnBuildRoute, Is.EqualTo("OnBuildRoute"));

            //フィールド自身のチェックで契約欠落・モジュール不在が出ないこと
            Assert.That(field.CheckDesign(new DesignCheckContext("Request", d, dbDefs))
                .Where(e => !e.Message.Contains("DbColumn") && !e.Message.Contains("DB")), Is.Empty);

            //編集ロックの既定条件 (Or 直下に葉4つ: null + 編集可能3状態)
            var condition = (MultiMatchCondition)request.DataWriteCondition.Condition!;
            Assert.That(condition.IsOrMatch, Is.True);
            var leaves = condition.Children.OfType<FieldValueMatchCondition>().ToList();
            Assert.That(leaves.Count, Is.EqualTo(4));
            Assert.That(leaves.All(e => e.SearchTargetVariable == "Approval.Status.Value"), Is.True);
            Assert.That(leaves.Count(e => e.Value is NullValue), Is.EqualTo(1));

            //クライアント評価用のロード同梱 + dotted 宣言。保存ファイルに書かれ、
            //読込時には "Approval.Status" のクローンフィールドとして合成される
            var requestJson = ReadModuleJson("Request");
            Assert.That(requestJson, Does.Contain("Approval.Status"));
            Assert.That(request.Fields.FirstOrDefault(e => e.Name == "Approval.Status"),
                Is.InstanceOf<SelectFieldDesign>());

            //レイアウト配置 + スクリプト雛形
            var grid = (GridLayoutDesign)request.DetailLayouts[""].Layout;
            Assert.That(grid.Rows.SelectMany(r => r.Columns)
                .Any(c => (c.Layout as FieldLayoutDesign)?.FieldName == "Approval"), Is.True);
            Assert.That(d.Scripts["Request"], Does.Contain("OnBuildRoute").And.Contain("new ApprovalRoute().Load("));
            Assert.That(d.Scripts["ApprovalRoute"], Does.Contain("ApprovalRouteData Load(").And.Contain("ModuleSearcher<ApprovalRouteStepMember>"));

            //PageFrame リンク
            var frame = d.PageFrames.Find("Main")!;
            Assert.That(frame.Left.Links.Select(e => e.Module),
                Does.Contain("ApprovalFlowMember").And.Contain("ApprovalFlow").And.Contain("ApprovalRoute"));

            //承認待ち一覧 / 承認フロー管理は一覧だけ (詳細遷移・削除なし)。承認待ち一覧は自分の Waiting 行だけ
            var memberLink = frame.Left.Links.First(e => e.Module == "ApprovalFlowMember");
            var memberList = (ListFieldDesign)memberLink.ListPageDesign.ListFieldDesign;
            Assert.That(memberLink.ListPageDesign.UseNavigateToCreate, Is.False);
            Assert.That(memberList.CanNavigateToDetail, Is.False);
            Assert.That(memberList.CanDelete, Is.False);
            var waiting = (MultiMatchCondition)memberList.SearchCondition.Condition!;
            Assert.That(waiting.Children.OfType<FieldVariableMatchCondition>().Single().Variable, Is.EqualTo("CurrentUser.Id.Value"));
            Assert.That(((StringValue)waiting.Children.OfType<FieldValueMatchConditionNonNull>().Single().Value!).Value, Is.EqualTo("Waiting"));
            var flowList = (ListFieldDesign)frame.Left.Links.First(e => e.Module == "ApprovalFlow").ListPageDesign.ListFieldDesign;
            Assert.That(flowList.CanNavigateToDetail, Is.False);
            Assert.That(flowList.SearchCondition.Condition, Is.Null);

            //DDL: 全テーブルの CREATE + 申請書の FK 列 ALTER
            var ddl = string.Join("\n", result.Ddl);
            foreach (var table in new[]
            {
                "approval_flows", "approval_flow_members", "approval_histories",
                "approval_routes", "approval_route_steps", "approval_route_step_members",
            })
            {
                Assert.That(ddl, Does.Contain($"CREATE TABLE {table}"), table);
            }
            Assert.That(ddl, Does.Contain("ALTER TABLE requests").And.Contain("approval_id"));
        }

        [Test]
        public void プレフィックスとユーザーモジュール差し替えが全参照に効く()
        {
            CreateFixture(userModuleName: "Staff", userNameField: "DisplayName", userEmailField: "MailAddress");
            var options = new ApprovalSetupOptions
            {
                DataSourceName = "Main",
                Prefix = "Keiri",
                UserModuleName = "Staff",
                UserDisplayNameField = "DisplayName",
                UserEmailField = "MailAddress",
            };
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[]
            {
                "KeiriApprovalFlow", "KeiriApprovalFlowMember", "KeiriApprovalHistory",
                "KeiriApprovalRoute", "KeiriApprovalRouteStep", "KeiriApprovalRouteStepMember",
            }));

            //生成ファイルにテンプレートのモジュール名参照が残っていないこと
            foreach (var name in result.CreatedModules)
            {
                var json = ReadModuleJson(name);
                Assert.That(json, Does.Not.Contain("\"AppUser\""), name);
                Assert.That(json, Does.Not.Contain("\"ApprovalFlow\""), name);
            }

            var d = Load();
            var flow = d.Modules.Find("KeiriApprovalFlow")!;
            Assert.That(flow.DbTable, Is.EqualTo("keiri_approval_flows"));

            //ユーザーモジュール差し替え (申請者リンクの参照先と表示フィールド)
            var applicant = (LinkFieldDesign)flow.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicant.SearchCondition.ModuleName, Is.EqualTo("Staff"));
            Assert.That(applicant.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));

            //フロー内の一覧参照がプレフィックス付きの生成先を指すこと
            var members = (ListFieldDesign)flow.Fields.First(e => e.Name == "Members");
            Assert.That(members.SearchCondition.ModuleName, Is.EqualTo("KeiriApprovalFlowMember"));

            //通知メールの宛先変数がメールアドレスフィールドに追従すること
            var member = d.Modules.Find("KeiriApprovalFlowMember")!;
            var mail = (MailFieldDesign)member.Fields.First(e => e is MailFieldDesign);
            Assert.That(mail.ToVariable, Is.EqualTo("ApproverUser.MailAddress.Value"));

            //スクリプトのモジュール名 (ModuleSearcher<Xxx>) も追従すること
            var script = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "KeiriApprovalFlowMember.mod.cs"));
            Assert.That(script, Does.Contain("ModuleSearcher<KeiriApprovalFlowMember>"));
            Assert.That(script, Does.Not.Contain("ModuleSearcher<ApprovalFlow>"));
            var routeScript = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "KeiriApprovalRoute.mod.cs"));
            Assert.That(routeScript, Does.Contain("ModuleSearcher<KeiriApprovalRoute>").And.Contain("ModuleSearcher<KeiriApprovalRouteStepMember>"));
            Assert.That(routeScript, Does.Not.Contain("<ApprovalRoute>"));
        }

        [Test]
        public void 冪等で二回目は生成もファイル変更もされない()
        {
            CreateFixture();
            ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);
            var before = ReadModuleJson("ApprovalFlow");
            var beforeRequest = ReadModuleJson("Request");

            var second = ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(second.CreatedModules, Is.Empty);
            Assert.That(second.SkippedModules.Count, Is.EqualTo(6));
            Assert.That(second.ParentWired, Is.False);
            Assert.That(ReadModuleJson("ApprovalFlow"), Is.EqualTo(before));
            Assert.That(ReadModuleJson("Request"), Is.EqualTo(beforeRequest));
        }

        [Test]
        public void 通知メールを外すとメンバーモジュールにメールフィールドが無い()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.UseTurnNotifyMail = false;
            ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var member = Load().Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.Fields.Any(e => e is MailFieldDesign), Is.False);
            Assert.That(ApprovalContracts.Member(member)!.TurnNotifyMail, Is.Empty);
        }

        [Test]
        public void 経路マスタなしはスクリプト組み立ての雛形になる()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.RouteMaster = ApprovalRouteMasterKind.None;
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[]
            {
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory",
            }));

            var d = Load();
            var field = d.Modules.Find("Request")!.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            Assert.That(d.Scripts["Request"], Does.Contain("NewRoute").And.Not.Contain(".Load("));
        }
    }
}
