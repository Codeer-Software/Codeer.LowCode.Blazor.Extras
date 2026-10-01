using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class AuditLogSetupServiceTest : SetupTestBase
    {
        static AuditLogSetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
        };

        [Test]
        public void 閲覧モジュールが揃いサーバーの列と一致する()
        {
            CreateFixture();
            var result = AuditLogSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[] { "AuditLog" }));

            var d = Load();
            var module = d.Modules.Find("AuditLog")!;
            Assert.That(module.DbTable, Is.EqualTo("audit_log"));
            Assert.That(module.DataSourceName, Is.EqualTo("Main"));

            //モジュールの列は、サーバー (DatabaseAuditSink) が書く固定列の中にある
            var ddlColumns = Regex.Matches(DatabaseAuditSink.CreateTableSql(DataSourceType.SQLite, "audit_log"), "^\\s*\"(\\w+)\"", RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value).ToList();
            var moduleColumns = module.Fields.OfType<DbValueFieldDesignBase>().Select(e => e.DbColumn).ToList();
            Assert.That(moduleColumns, Is.Not.Empty.And.SubsetOf(ddlColumns));
            Assert.That(moduleColumns, Does.Contain("occurred_at_utc").And.Contain("targets").And.Contain("detail"));

            //日時は UTC で入っているので表示はローカルに直す。分類・結果はサーバーの enum と同じ候補
            var occurredAt = (DateTimeFieldDesign)module.Fields.Single(e => e.Name == "OccurredAt");
            Assert.That(occurredAt.SaveAsUtc, Is.True);
            var category = (SelectFieldDesign)module.Fields.Single(e => e.Name == "Category");
            Assert.That(category.Candidates, Is.EquivalentTo(Enum.GetNames<AuditCategory>()));
            var auditResult = (SelectFieldDesign)module.Fields.Single(e => e.Name == "Result");
            Assert.That(auditResult.Candidates, Is.EquivalentTo(Enum.GetNames<AuditResult>()));

            //操作者はユーザーモジュールへのリンク。監査ログは追記専用 = 画面からは誰も書けない
            var user = (LinkFieldDesign)module.Fields.Single(e => e.Name == "UserId");
            Assert.That(user.SearchCondition.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(user.DisplayTextVariable, Is.EqualTo("Name.Value"));
            Assert.That((module.CanCreate, module.CanUpdate, module.CanDelete), Is.EqualTo((false, false, false)));
            Assert.That(module.UserWriteCondition.ModuleName, Is.EqualTo("AppUser"));
            var protect = (FieldValueMatchCondition)module.UserWriteCondition.Condition!;
            Assert.That(protect.SearchTargetVariable, Is.EqualTo("Id.Value"));

            //PageFrame リンク (新規作成・削除なし・詳細遷移あり・Id の降順) と DDL (テーブル + 日時のインデックス)
            var pageLink = d.PageFrames.Find("Main")!.Left.Links.Single(e => e.Module == "AuditLog");
            Assert.That(pageLink.ListPageDesign.UseNavigateToCreate, Is.False);
            var list = (ListFieldDesign)pageLink.ListPageDesign.ListFieldDesign;
            Assert.That((list.CanNavigateToDetail, list.CanCreate, list.CanUpdate, list.CanDelete), Is.EqualTo((true, false, false, false)));
            Assert.That(list.SearchCondition.SortConditions.Single().IsDescending, Is.True);
            var ddl = string.Join("\n", result.Ddl);
            Assert.That(ddl, Does.Contain("create table \"audit_log\""));
            Assert.That(result.Ddl.Last(), Is.EqualTo("create index \"ix_audit_log_occurred_at\" on \"audit_log\" (\"occurred_at_utc\");"));

            //次にやること (appsettings の有効化・閲覧の制限・追記専用の担保)
            var notes = string.Join("\n", result.Notes);
            Assert.That(notes, Does.Contain("AuditLog.Enabled").And.Contain("UserReadCondition").And.Contain("RetentionDays"));
        }

        [TestCase(DataSourceType.SQLite)]
        [TestCase(DataSourceType.SQLServer)]
        [TestCase(DataSourceType.PostgreSQL)]
        [TestCase(DataSourceType.MySQL)]
        [TestCase(DataSourceType.Oracle)]
        public void DDLはサーバーのDatabaseAuditSinkと同じ(DataSourceType type)
        {
            CreateFixture();
            var options = DefaultOptions();
            options.TableName = "app_audit";
            var result = AuditLogSetupService.Run(Load(), ProjectDir, options, type);

            var expected = DatabaseAuditSink.CreateTableSql(type, "app_audit").ReplaceLineEndings("\n") + ";\n"
                + DatabaseAuditSink.CreateIndexSql(type, "app_audit") + ";";
            Assert.That(string.Join("\n", result.Ddl), Is.EqualTo(expected));
            Assert.That(Load().Modules.Find("AuditLog")!.DbTable, Is.EqualTo("app_audit"));
        }

        [Test]
        public void ユーザーモジュールと表示名フィールドを差し替えられる()
        {
            CreateFixture(userModuleName: "Member", userNameField: "DisplayName", userEmailField: "Mail");
            var options = DefaultOptions();
            options.UserModuleName = "Member";
            options.UserDisplayNameField = "DisplayName";
            AuditLogSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var module = Load().Modules.Find("AuditLog")!;
            var user = (LinkFieldDesign)module.Fields.Single(e => e.Name == "UserId");
            Assert.That(user.SearchCondition.ModuleName, Is.EqualTo("Member"));
            Assert.That(user.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));
            Assert.That(module.UserWriteCondition.ModuleName, Is.EqualTo("Member"));
        }

        [Test]
        public void 同名モジュールがあれば生成しない()
        {
            CreateFixture();
            AuditLogSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);
            var before = ReadModuleJson("AuditLog");

            var result = AuditLogSetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.Empty);
            Assert.That(result.SkippedModules, Is.EquivalentTo(new[] { "AuditLog" }));
            Assert.That(ReadModuleJson("AuditLog"), Is.EqualTo(before));
        }

        [Test]
        public void テーブルが既にあればDDLを出さない()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.AddPageFrameLink = false;
            var existing = new List<DbTableDefinition> { new() { Name = "AUDIT_LOG" } };
            var result = AuditLogSetupService.Run(Load(), ProjectDir, options, DataSourceType.PostgreSQL, existing);

            Assert.That(result.Ddl, Is.Empty);
            Assert.That(string.Join("\n", result.Notes), Does.Contain("既にある"));
            Assert.That(Load().PageFrames.Find("Main")!.Left.Links.Any(e => e.Module == "AuditLog"), Is.False);
        }
    }
}
