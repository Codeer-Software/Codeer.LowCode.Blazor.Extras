using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Extras.Test.Tag;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// TagField のタグ付け行は従属レコードの宣言 (契約の役割から組み立てた結び付き) で版に入り、差分はタグ名の行の追加 / 削除で出て、復元で版の行に差し替わる。
    /// </summary>
    public class EditHistoryTagFieldTest
    {
        static ModuleData TagRow(string id, string name)
        {
            var data = new ModuleData { Name = "ContactTags" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["OwnerId"] = new IdFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = name };
            return data;
        }

        static ModuleData Contact(params ModuleData[] tags)
        {
            var data = new ModuleData { Name = "Contact" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = "A" };
            data.Fields["Tags"] = new ListFieldData { Children = tags.ToList() };
            return data;
        }

        [Test]
        public void タグ付け行を従属レコードとして宣言する_契約が無ければ宣言しない()
        {
            var d = TagTestDesigns.Create();
            var tags = (TagFieldDesign)d.Modules.Find("Contact")!.Fields.Single(e => e.Name == "Tags");
            var owned = tags.GetOwnedRecords(d.Modules).Single();
            Assert.That((owned.Name, owned.Condition.ModuleName, owned.HoldsAllRecords), Is.EqualTo(("Tags", "ContactTags", true)));
            Assert.That(owned.Condition.GetFieldVariableConditions().Single().SearchTargetVariable, Is.EqualTo("OwnerId.Value"), "検索・同梱と同じ結び付き");

            var noContract = TagTestDesigns.Create(e => e.TagModuleName = "Contact");
            Assert.That(((TagFieldDesign)noContract.Modules.Find("Contact")!.Fields.Single(e => e.Name == "Tags")).GetOwnedRecords(noContract.Modules), Is.Empty);
        }

        [Test]
        public void 差分はタグの行の追加と削除で出てOwnerIdは出さない()
        {
            var design = TagTestDesigns.Create();
            var before = Contact(TagRow("11", "展示会"), TagRow("12", "DXPO"));
            var after = Contact(TagRow("11", "展示会"), TagRow("13", "セミナー"));

            var changes = EditHistoryDiff.Compute(design, design.Modules.Find("Contact")!, before, after, (_, _) => true);
            var change = changes.Single();
            Assert.That((change.FieldName, change.DisplayName, change.IsList, change.AddedCount, change.RemovedCount), Is.EqualTo(("Tags", "タグ", true, 1, 1)));
            Assert.That(change.Rows.Select(e => (e.Kind, ((TextFieldData)e.Row.Fields["Name"]).Value)),
                Is.EquivalentTo(new[] { (EditHistoryRowChangeKind.Added, "セミナー"), (EditHistoryRowChangeKind.Removed, "DXPO") }));
            Assert.That(change.Rows.SelectMany(e => e.Changes).Select(e => e.FieldName), Does.Not.Contain("OwnerId"), "親へのリンクは行の値として出さない");
        }

        [Test]
        public async Task 復元は版のタグに差し替える()
        {
            var design = TagTestDesigns.Create();
            var services = new TestServices(design);
            //DB: 展示会 (11), DXPO (12)
            var reads = 0;
            services.App.ListProvider = request =>
            {
                if (request.Condition.ModuleName != "ContactTags") return new Paging<ModuleData>();
                reads++;
                return new Paging<ModuleData> { TotalCount = 2, Items = [TagRow("11", "展示会"), TagRow("12", "DXPO")] };
            };

            //タグ付け行を同梱せずに読んだレコード (今のタグは復元の前に 1 回読む)
            var current = Contact();
            current.Fields.Remove("Tags");
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, current, ModuleLayoutType.None);
            var tags = module.GetField<TagField>("Tags")!;
            Assert.That(reads, Is.EqualTo(0), "表示のためには読まない");

            //版: 展示会 (11), セミナー (13: 今は無い)
            var snapshot = Contact(TagRow("11", "展示会"), TagRow("13", "セミナー"));
            snapshot.Fields.Remove("Name");
            var applied = await EditHistoryRestorer.ApplyAsync(module, snapshot, null);
            Assert.That(applied, Is.EqualTo(1), "タグの宣言 1 つを差し替えた");
            Assert.That(tags.Tags, Is.EqualTo(new[] { "展示会", "セミナー" }));
            Assert.That(reads, Is.EqualTo(1), "復元の前に今のタグ付け行を 1 回読む");

            var submit = tags.GetSubmitData();
            Assert.That(submit.Delete.Select(e => e.Id), Is.EqualTo(new[] { "12" }), "版に無いタグは削除");
            var added = submit.Add.Single();
            Assert.That(((TextFieldData)added.Fields["Name"]).Value, Is.EqualTo("セミナー"));
            Assert.That(((IdFieldData)added.Fields["OwnerId"]).Value, Is.EqualTo("1"), "親の Id を入れる");
        }
    }
}
