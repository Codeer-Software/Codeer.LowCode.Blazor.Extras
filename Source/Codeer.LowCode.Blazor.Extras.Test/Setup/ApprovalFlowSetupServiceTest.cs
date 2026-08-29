using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class ApprovalFlowSetupServiceTest : SetupTestBase
    {
        static ApprovalSetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
        };

        [Test]
        public void 既定構成で承認モジュール一式が生成され契約が解決する()
        {
            CreateFixture();
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[]
            {
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory", "MyApprovalList", "ApprovalStatusList",
                "ApprovalRoute", "ApprovalRouteStep", "ApprovalRouteStepMember",
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

            //申請書側は触らない (ユーザーの作業。手順は Notes で案内)
            var request = d.Modules.Find("Request")!;
            Assert.That(request.Fields.OfType<ApprovalFlowFieldDesign>().Any(), Is.False);
            Assert.That(request.DataWriteCondition.Condition, Is.Null);
            Assert.That(d.Scripts.ContainsKey("Request"), Is.False);
            Assert.That(string.Join("\n", result.Notes), Does.Contain("ApprovalFlowField").And.Contain("OnBuildRoute").And.Contain("new ApprovalRoute().Load("));

            //経路マスタの読み込みスクリプト
            Assert.That(d.Scripts["ApprovalRoute"], Does.Contain("ApprovalRouteData Load(").And.Contain("ModuleSearcher<ApprovalRouteStepMember>"));

            //PageFrame リンク
            var frame = d.PageFrames.Find("Main")!;
            Assert.That(frame.Left.Links.Select(e => e.Module),
                Is.EquivalentTo(new[] { "MyApprovalList", "ApprovalStatusList", "ApprovalRoute" }));

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
            //承認状況の検索 (状態 / 申請者 / 申請種別) は出力列への通常検索。申請種別 = 申請書モジュール名の enum (メンバーはユーザーが足す)
            var statusList = d.Modules.Find("ApprovalStatusList")!;
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "TargetModuleName")).EnumName, Is.EqualTo("ApprovalTargetModule"));
            Assert.That(((SelectFieldDesign)d.Modules.Find("MyApprovalList")!.Fields.First(e => e.Name == "TargetModuleName")).EnumName,
                Is.EqualTo("ApprovalTargetModule"));
            var requestType = d.Enums.Single(e => e.Name == "ApprovalTargetModule");
            Assert.That(requestType.Members, Is.Empty);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Enums", "ApprovalTargetModule.enum.json")), Is.True);
            var applicant = (SelectFieldDesign)statusList.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicant.SearchCondition.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(((SelectFieldDesign)statusList.Fields.First(e => e.Name == "Status")).AllowOrSearch, Is.True);

            //DDL: 全テーブルの CREATE
            var ddl = string.Join("\n", result.Ddl);
            foreach (var table in new[]
            {
                "approval_flows", "approval_flow_members", "approval_histories",
                "approval_routes", "approval_route_steps", "approval_route_step_members",
            })
            {
                Assert.That(ddl, Does.Contain($"CREATE TABLE {table}"), table);
            }
            Assert.That(ddl, Does.Not.Contain("ALTER TABLE"));
        }

        [Test]
        public void ユーザーモジュール差し替えが全参照に効く()
        {
            CreateFixture(userModuleName: "Staff", userNameField: "DisplayName", userEmailField: "MailAddress");
            var options = new ApprovalSetupOptions
            {
                DataSourceName = "Main",
                UserModuleName = "Staff",
                UserDisplayNameField = "DisplayName",
                UserEmailField = "MailAddress",
            };
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            //生成ファイルにテンプレートのユーザーモジュール名参照が残っていないこと
            foreach (var name in result.CreatedModules)
                Assert.That(ReadModuleJson(name), Does.Not.Contain("\"AppUser\""), name);

            var d = Load();
            var flow = d.Modules.Find("ApprovalFlow")!;

            //ユーザーモジュール差し替え (申請者リンクの参照先と表示フィールド)
            var applicant = (LinkFieldDesign)flow.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicant.SearchCondition.ModuleName, Is.EqualTo("Staff"));
            Assert.That(applicant.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));

            //通知メールの宛先変数がメールアドレスフィールドに追従すること
            var member = d.Modules.Find("ApprovalFlowMember")!;
            var mail = (MailFieldDesign)member.Fields.First(e => e is MailFieldDesign);
            Assert.That(mail.ToVariable, Is.EqualTo("ApproverUser.MailAddress.Value"));

            //検索用モジュールの SQL もユーザーモジュールのテーブル・表示名列を指すこと
            var inboxSql = File.ReadAllText(Path.Combine(ProjectDir, "Modules", "MyApprovalList.Query.sql"));
            Assert.That(inboxSql, Does.Contain("LEFT JOIN app_users u").And.Contain("u.name AS applicant_name"));
            //申請者の検索候補もユーザーモジュール (Staff) を指し、表示名フィールドに追従する
            var applicantSearch = (SelectFieldDesign)d.Modules.Find("ApprovalStatusList")!.Fields.First(e => e.Name == "Applicant");
            Assert.That(applicantSearch.SearchCondition.ModuleName, Is.EqualTo("Staff"));
            Assert.That(applicantSearch.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));
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
            Assert.That(second.SkippedModules.Count, Is.EqualTo(8));
            Assert.That(ReadModuleJson("ApprovalFlow"), Is.EqualTo(before));
            Assert.That(ReadModuleJson("Request"), Is.EqualTo(beforeRequest));
        }

        [Test]
        public void メールを使わないとメンバーモジュールにメールフィールドが無い()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.UseTurnNotifyMail = false;
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var d = Load();
            var member = d.Modules.Find("ApprovalFlowMember")!;
            Assert.That(member.Fields.Any(e => e is MailFieldDesign), Is.False);
            Assert.That(ApprovalContracts.Member(member)!.TurnNotifyMail, Is.Empty);
            Assert.That(string.Join("\n", result.Notes), Does.Not.Contain("メールのセットアップ"));
        }

        [Test]
        public void メールを使ってもメール側は作らず案内だけ出す()
        {
            CreateFixture();
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            var d = Load();
            Assert.That(d.Modules.Find("ApprovalFlowMember")!.Fields.Any(e => e is MailFieldDesign), Is.True);
            Assert.That(d.Modules.Find("MailHistory"), Is.Null);
            Assert.That(string.Join("\n", result.Notes), Does.Contain("メールのセットアップ"));
        }

        [Test]
        public void 経路マスタなしは経路モジュールを作らずスクリプト組み立ての案内になる()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.RouteMaster = ApprovalRouteMasterKind.None;
            var result = ApprovalFlowSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[]
            {
                "ApprovalFlow", "ApprovalFlowMember", "ApprovalHistory", "MyApprovalList", "ApprovalStatusList",
            }));
            Assert.That(string.Join("\n", result.Notes), Does.Contain("new ApprovalRouteData()").And.Not.Contain(".Load("));
            Assert.That(Load().PageFrames.Find("Main")!.Left.Links.Select(e => e.Module), Does.Not.Contain("ApprovalRoute"));
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

            //Id(数値) と FK(文字列) の結合は PostgreSQL だけ暗黙変換されないので Id 側にキャストが付く。SELECT 句の別名付けは対象外
            var idJoin = type == DataSourceType.PostgreSQL ? "f.id::text = m.flow_id" : "f.id = m.flow_id";
            Assert.That(myList, Does.Contain(idJoin).And.Contain("f.id AS flow_id").And.Not.Contain("id::text AS"));
            var userJoin = type == DataSourceType.PostgreSQL ? "u.id::text = f.applicant" : "u.id = f.applicant";
            Assert.That(statusList, Does.Contain(userJoin).And.Contain("f.id AS id").And.Not.Contain("id::text AS"));
            if (type == DataSourceType.PostgreSQL) Assert.That(statusList, Does.Contain("u2.id::text = m2.approver_user").And.Contain("h.flow_id = f.id::text"));
        }
    }
}
