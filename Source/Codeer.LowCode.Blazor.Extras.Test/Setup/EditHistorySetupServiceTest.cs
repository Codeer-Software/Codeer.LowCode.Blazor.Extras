using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class EditHistorySetupServiceTest : SetupTestBase
    {
        static EditHistorySetupOptions DefaultOptions() => new()
        {
            DataSourceName = "Main",
        };

        [Test]
        public void 履歴モジュールが揃い契約が解決する()
        {
            CreateFixture();
            var result = EditHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.EquivalentTo(new[] { "EditHistory" }));

            var d = Load();
            var module = d.Modules.Find("EditHistory")!;
            Assert.That(module.DbTable, Is.EqualTo("edit_histories"));
            Assert.That(module.DataSourceName, Is.EqualTo("Main"));
            Assert.That(EditHistoryContracts.Contract(module), Is.Not.Null);

            //契約チェックが通ること (既定役割のフィールドが全部あり、型が合うこと)
            var dbDefs = new Dictionary<string, List<DbTableDefinition>>();
            Assert.That(module.Fields.OfType<EditHistoryContractFieldDesign>().Single()
                .CheckDesign(new DesignCheckContext("EditHistory", d, dbDefs)), Is.Empty);

            //復活ボタンと対象レコードリンクが同梱され、履歴モジュール上なので指摘なし
            var restore = module.Fields.OfType<EditHistoryRestoreButtonFieldDesign>().Single();
            Assert.That(restore.CheckDesign(new DesignCheckContext("EditHistory", d, dbDefs)), Is.Empty);
            var link = module.Fields.OfType<EditHistoryTargetLinkFieldDesign>().Single();
            Assert.That(link.CheckDesign(new DesignCheckContext("EditHistory", d, dbDefs)), Is.Empty);
            Assert.That(module.ListLayouts[""].Elements[0].Select(e => e.FieldName), Does.Contain(link.Name));

            //ModuleName は対象モジュール enum を参照する Select。enum は空で生成 (メンバーはユーザーが対象モジュールごとに足す)
            var moduleNameField = (SelectFieldDesign)module.Fields.Single(e => e.Name == "ModuleName");
            Assert.That(moduleNameField.EnumName, Is.EqualTo("EditHistoryTargetModule"));
            var enumDesign = d.Enums.Single(e => e.Name == "EditHistoryTargetModule");
            Assert.That(enumDesign.Members, Is.Empty);
            Assert.That(File.Exists(Path.Combine(ProjectDir, "Enums", "EditHistoryTargetModule.enum.json")));

            //変更者はユーザーモジュールへのリンク。履歴はシステムの記録 = 画面からは誰も書けない
            var user = (LinkFieldDesign)module.Fields.Single(e => e.Name == "UserId");
            Assert.That(user.SearchCondition.ModuleName, Is.EqualTo("AppUser"));
            Assert.That(user.DisplayTextVariable, Is.EqualTo("Name.Value"));
            Assert.That(module.UserWriteCondition.ModuleName, Is.EqualTo("AppUser"));
            var protect = (FieldValueMatchCondition)module.UserWriteCondition.Condition!;
            Assert.That(protect.SearchTargetVariable, Is.EqualTo("Id.Value"));

            //PageFrame リンク (新規作成なし・詳細遷移あり) と DDL
            var pageLink = d.PageFrames.Find("Main")!.Left.Links.Single(e => e.Module == "EditHistory");
            Assert.That(pageLink.ListPageDesign.UseNavigateToCreate, Is.False);
            var list = (ListFieldDesign)pageLink.ListPageDesign.ListFieldDesign;
            Assert.That(list.CanNavigateToDetail, Is.True);
            Assert.That(list.CanDelete, Is.False);
            Assert.That(string.Join("\n", result.Ddl), Does.Contain("CREATE TABLE edit_histories"));

            //次にやること (対象モジュールへの配置・サーバー結線・enum メンバー)
            var notes = string.Join("\n", result.Notes);
            Assert.That(notes, Does.Contain("EditHistoryField").And.Contain("EditHistoryRecorder").And.Contain("EditHistoryTargetModule"));
        }

        [Test]
        public void ユーザーモジュールと表示名フィールドを差し替えられる()
        {
            CreateFixture(userModuleName: "Member", userNameField: "DisplayName", userEmailField: "Mail");
            var options = DefaultOptions();
            options.UserModuleName = "Member";
            options.UserDisplayNameField = "DisplayName";
            EditHistorySetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var module = Load().Modules.Find("EditHistory")!;
            var user = (LinkFieldDesign)module.Fields.Single(e => e.Name == "UserId");
            Assert.That(user.SearchCondition.ModuleName, Is.EqualTo("Member"));
            Assert.That(user.DisplayTextVariable, Is.EqualTo("DisplayName.Value"));
            Assert.That(module.UserWriteCondition.ModuleName, Is.EqualTo("Member"));
        }

        [Test]
        public void enumを作らなければModuleNameは素の名前で運用()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.CreateTargetModuleEnum = false;
            options.AddPageFrameLink = false;
            var result = EditHistorySetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var d = Load();
            var moduleNameField = (SelectFieldDesign)d.Modules.Find("EditHistory")!.Fields.Single(e => e.Name == "ModuleName");
            Assert.That(moduleNameField.EnumName, Is.Empty);
            Assert.That(d.Enums.Any(e => e.Name == "EditHistoryTargetModule"), Is.False);
            Assert.That(d.PageFrames.Find("Main")!.Left.Links.Any(e => e.Module == "EditHistory"), Is.False);
            Assert.That(string.Join("\n", result.Notes), Does.Not.Contain("EditHistoryTargetModule"));
        }

        [Test]
        public void 同名モジュールがあれば生成しない()
        {
            CreateFixture();
            EditHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);
            var before = ReadModuleJson("EditHistory");

            var result = EditHistorySetupService.Run(Load(), ProjectDir, DefaultOptions(), DataSourceType.SQLite);

            Assert.That(result.CreatedModules, Is.Empty);
            Assert.That(result.SkippedModules, Is.EquivalentTo(new[] { "EditHistory" }));
            Assert.That(result.Ddl, Is.Empty);
            Assert.That(ReadModuleJson("EditHistory"), Is.EqualTo(before));
        }

        [Test]
        public void 履歴モジュール名を変えるとテーブル名も追従する()
        {
            CreateFixture();
            var options = DefaultOptions();
            options.HistoryModuleName = "ChangeLog";
            EditHistorySetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            var module = Load().Modules.Find("ChangeLog")!;
            Assert.That(module.DbTable, Is.EqualTo("change_logs"));
            Assert.That(EditHistoryContracts.Contract(module), Is.Not.Null);
        }
    }
}
