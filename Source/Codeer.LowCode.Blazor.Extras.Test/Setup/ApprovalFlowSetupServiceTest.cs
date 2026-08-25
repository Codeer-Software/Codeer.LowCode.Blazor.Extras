using Codeer.LowCode.Blazor.DesignLogic;
﻿using Codeer.LowCode.Blazor.DataIO.Db.Definition;
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
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory", "MyApprovalList", "ApprovalStatusList",
                "ApprovalRoute", "ApprovalRouteStep", "ApprovalRouteStepMember", "MailHistory",
            }));
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalFlow.mod.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalRoute.mod.cs")), Is.True);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "MyApprovalList.Query.sql")), Is.True);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "ApprovalStatusList.Query.sql")), Is.True);

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
                Is.EquivalentTo(new[] { "MyApprovalList", "ApprovalStatusList", "ApprovalRoute", "MailHistory" }));

            //エンジン用モジュールは UI を持たない (一覧列・検索レイアウト・スクリプトなし)
            Assert.That(flow.ListLayouts[""].Elements.SelectMany(e => e).All(e => string.IsNullOrEmpty(e.FieldName)), Is.True);
            Assert.That(flow.SearchLayouts.Values.All(e => e.Layout.GetDescendantLayouts<FieldLayoutDesign>().Count == 0), Is.True);
            Assert.That(d.Scripts.ContainsKey("ApprovalFlow"), Is.False);

            //検索用モジュールは一覧だけ (詳細遷移・作成・削除なし)。「開く」で申請書へ
            foreach (var name in new[] { "MyApprovalList", "ApprovalStatusList" })
            {
                var link = frame.Left.Links.First(e => e.Module == name);
                var list = (ListFieldDesign)link.ListPageDesign.ListFieldDesign;
                Assert.That(link.ListPageDesign.UseNavigateToCreate, Is.False, name);
                Assert.That(list.CanNavigateToDetail, Is.False, name);
                Assert.That(list.CanDelete, Is.False, name);
                Assert.That(list.SearchCondition.SortConditions.Single().Variable, Is.EqualTo("SubmittedAt.Value"), name);
                var query = d.Modules.Find(name)!;
                Assert.That(query.DbTable, Is.Empty, name);
                Assert.That(query.Fields.OfType<QueryFieldDesign>().Single().QuerySetting.Parameters.Any(e => e.IsParameter == false && e.Name == "target_id"), Is.True, name);
                Assert.That(d.Scripts[name], Does.Contain("NavigationService.GetModuleDataUrl(TargetModuleName.Value, TargetId.Value)"), name);
            }
            //承認待ちはログインユーザーで絞る (サーバー束縛の予約パラメータ)
            var inboxSql = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "MyApprovalList.Query.sql"));
            Assert.That(inboxSql, Does.Contain("@current_user_id").And.Contain("LEFT JOIN app_users u").And.Contain("u.name AS applicant_name"));
            //承認状況の検索 (状態 / 申請者 / 申請種別) は出力列への通常検索。申請種別 = 申請書モジュール名の enum (結線先が最初のメンバー)
            var statusList = d.Modules.Find("ApprovalStatusList")!;
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "TargetModuleName")).EnumName, Is.EqualTo("ApprovalRequestType"));
            Assert.That(((SelectFieldDesign)d.Modules.Find("MyApprovalList")!.Fields.First(e => e.Name == "TargetModuleName")).EnumName,
                Is.EqualTo("ApprovalRequestType"));
            var requestType = d.Enums.Single(e => e.Name == "ApprovalRequestType");
            Assert.That(requestType.Members.Select(e => e.Name), Is.EqualTo(new[] { "Request" }));
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Enums", "ApprovalRequestType.enum.json")), Is.True);
            var applicant = (SelectFieldDesign)statusList.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicant.SearchCondition.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "Status")).AllowOrSearch, Is.True);

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
                "KeiriApprovalFlow", "KeiriApprovalFlowMember", "KeiriApprovalHistory", "KeiriMyApprovalList", "KeiriApprovalStatusList",
                "KeiriApprovalRoute", "KeiriApprovalRouteStep", "KeiriApprovalRouteStepMember", "MailHistory",
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

            //検索用モジュールの SQL もプレフィックス付きテーブルとユーザーモジュールのテーブル・表示名列を指すこと
            var inboxSql = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "KeiriMyApprovalList.Query.sql"));
            Assert.That(inboxSql, Does.Contain("FROM keiri_approval_flow_members m").And.Contain("JOIN keiri_approval_flows f")
                .And.Contain("FROM keiri_approval_histories h").And.Contain("LEFT JOIN app_users u").And.Contain("u.name AS applicant_name"));
            //申請者の検索候補もプレフィックス付きでないユーザーモジュール (Staff) を指し、表示名フィールドに追従する
            var applicantSearch = (SelectFieldDesign)d.Modules.Find("KeiriApprovalStatusList")!.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicantSearch.SearchCondition.ModuleName, Is.EqualTo("Staff"));
            Assert.That(applicantSearch.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));
            //申請種別 enum もプレフィックス付き
            Assert.That(((SelectFieldDesign)d.Modules.Find("KeiriApprovalStatusList")!.Fields.First(e => e.Name == "TargetModuleName")).EnumName,
                Is.EqualTo("KeiriApprovalRequestType"));
            Assert.That(d.Enums.Any(e => e.Name == "KeiriApprovalRequestType"), Is.True);
            Assert.That(inboxSql, Does.Not.Contain(" approval_flows ").And.Not.Contain(" approval_flow_members "));
            //スクリプトのモジュール名 (ModuleSearcher<Xxx>) も追従すること
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
            Assert.That(second.SkippedModules.Count, Is.EqualTo(9));
            Assert.That(second.ParentWired, Is.False);
            Assert.That(ReadModuleJson("ApprovalFlow"), Is.EqualTo(before));
            Assert.That(ReadModuleJson("Request"), Is.EqualTo(beforeRequest));
        }

        [Test]
        public void メールを使わないとメンバーモジュールにメールフィールドが無く履歴も差出人契約も作らない()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.UseTurnNotifyMail = false;
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var d = Load();
            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.Fields.Any(e => e is MailFieldDesign), Is.False);
            Assert.That(ApprovalContracts.Member(member)!.TurnNotifyMail, Is.Empty);
            Assert.That(d.Modules.Find("MailHistory"), Is.Null);
            Assert.That(d.Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().Any(), Is.False);
            Assert.That(string.Join("\n", result.Notes), Does.Not.Contain("DefaultInfraName"));
        }

        [Test]
        public void メールを使うと差出人契約と送信履歴も揃いサーバー設定の案内が出る()
        {
            CreateFixture();
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Does.Contain("MailHistory"));
            var d = Load();
            var contract = d.Modules.Find("AppUser")!.Fields.OfType<MailSenderContractFieldDesign>().Single();
            Assert.That(contract.Email, Is.EqualTo("Email.Value"));
            Assert.That(contract.DisplayName, Is.EqualTo("Name.Value"));
            Assert.That(d.PageFrames.Find("Main")!.Left.Links.Select(e => e.Module), Does.Contain("MailHistory"));
            Assert.That(string.Join("\n", result.Notes), Does.Contain("HistoryModuleName").And.Contain("DefaultInfraName"));
            Assert.That(string.Join("\n", result.Ddl), Does.Contain("mail_histories"));

            //履歴だけ外せる
            var options = DefaultOptions();
            options.UseMailHistory = false;
            options.Prefix = "X";
            var second = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);
            Assert.That(second.CreatedModules, Does.Not.Contain("MailHistory"));
            Assert.That(string.Join("\n", second.Notes), Does.Not.Contain("HistoryModuleName"));
        }

        [Test]
        public void ユーザーモジュールに差出人契約があればその宣言をユーザー項目に使う()
        {
            CreateFixture(userModuleName: "Staff", userNameField: "DisplayName", userEmailField: "MailAddress");
            var staff = Load().Modules.Find("Staff")!;
            staff.Fields.Add(new MailSenderContractFieldDesign { Name = "MailSender", Email = "MailAddress.Value", DisplayName = "DisplayName.Value" });
            SaveModule(staff);

            //オプションは既定 (Name / Email) のまま = 契約の宣言が勝つ
            var options = new ApprovalSetupOptions { DataSourceName = "Main", UserModuleName = "Staff" };
            ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var d = Load();
            var mail = d.Modules.Find("ApprovalFlowMember")!.Fields.OfType<MailFieldDesign>().Single();
            Assert.That(mail.ToVariable, Is.EqualTo("ApproverUser.MailAddress.Value"));
            Assert.That(d.Modules.Find("Staff")!.Fields.OfType<MailSenderContractFieldDesign>().Count(), Is.EqualTo(1));
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
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory", "MyApprovalList", "ApprovalStatusList", "MailHistory",
            }));

            var d = Load();
            var field = d.Modules.Find("Request")!.Fields.OfType<ApprovalFlowFieldDesign>().Single();
            Assert.That(d.Scripts["Request"], Does.Contain("NewRoute").And.Not.Contain(".Load("));
        }
    

        [TestCase(DataSourceType.SQLite, "GROUP_CONCAT(u2.name, '、')", "@current_user_id")]
        [TestCase(DataSourceType.PostgreSQL, "STRING_AGG(u2.name, '、')", "@current_user_id")]
        [TestCase(DataSourceType.SQLServer, "STRING_AGG(u2.name, '、')", "@current_user_id")]
        [TestCase(DataSourceType.MySQL, "GROUP_CONCAT(u2.name SEPARATOR '、')", "@current_user_id")]
        [TestCase(DataSourceType.Oracle, "LISTAGG(u2.name, '、') WITHIN GROUP (ORDER BY u2.name)", ":current_user_id")]
        public void 検索用SQLはDBの方言に合わせて書き換わる(DataSourceType type, string concat, string userParam)
        {
            CreateFixture();
            ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), type);

            var statusList = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "ApprovalStatusList.Query.sql"));
            Assert.That(statusList, Does.Contain(concat));
            var myList = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "MyApprovalList.Query.sql"));
            Assert.That(myList, Does.Contain(userParam).And.Contain("LEFT JOIN app_users u").And.Contain("u.name AS applicant_name"));
        }
}
}
