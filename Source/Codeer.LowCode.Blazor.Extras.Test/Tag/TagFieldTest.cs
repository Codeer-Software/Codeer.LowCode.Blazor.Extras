using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>TagField の DB を使わない部分: 保存しない入力欄 (結び付きなし)、タグの区切り、デザインチェック (TagField と 2 つの契約)、リネーム追従。</summary>
    public class TagFieldTest
    {
        static readonly Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>> NoTables = new();

        static ModuleData MasterRow(string id, string name)
        {
            var row = new ModuleData { Name = "Tag" };
            row.Fields["Id"] = new IdFieldData { Value = id };
            row.Fields["Name"] = new TextFieldData { Value = name };
            return row;
        }

        //マスタの読み込みはハーネスが返す (展示会 / DXPO)
        static async Task<(TestServices Services, TagField Field)> CreatePickerAsync(Action<TagFieldDesign>? customize = null)
        {
            var services = new TestServices(TagTestDesigns.Create(customizePicker: customize));
            services.App.ListProvider = r => r.Condition.ModuleName == "Tag"
                ? new Paging<ModuleData> { Items = [MasterRow("1", "展示会"), MasterRow("2", "DXPO")] }
                : new Paging<ModuleData>();
            var module = await services.CreateModuleAsync("Picker", ModuleLayoutType.Detail);
            return (services, (TagField)module.GetField("Pick")!);
        }

        static List<DesignCheckInfo> Check(DesignData d, string module, string field)
            => d.Modules.Find(module)!.Fields.Single(e => e.Name == field).CheckDesign(new DesignCheckContext(module, d, NoTables));

        #region 保存しない入力欄 (結び付きなし)

        [Test]
        public async Task 入力欄はタグ名を持つだけで保存しない()
        {
            var (services, field) = await CreatePickerAsync();
            Assert.That(field.IsBound, Is.False);
            await field.AddTagAsync("dxpo");
            Assert.That(services.App.ListRequests, Is.Empty, "新しいタグを入れてよい入力欄は、足すだけでは問い合わせない (一覧の行の表示用に並ぶため)");
            Assert.That(field.Tags, Is.EqualTo(new[] { "dxpo" }));
            await field.RemoveTagAsync("DXPO");

            await field.GetCandidatesAsync();
            await field.AddTagAsync("dxpo、新規 ,展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "新規", "展示会" }), "候補を読んでいればマスタの表記");
            Assert.That(field.IsModified, Is.True);
            Assert.That(field.GetSubmitData().Add, Is.Empty);
            Assert.That(services.App.ListRequests.All(e => e.Condition.ModuleName == "Tag"), Is.True, "タグ付けは読まない");

            await field.RemoveTagAsync("新規");
            Assert.That(field.HasTag("展示会"), Is.True);
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "展示会" }));
        }

        [Test]
        public async Task 入力欄でもAllowNewTagsがfalseならマスタのタグだけ()
        {
            var (_, field) = await CreatePickerAsync(e => e.AllowNewTags = false);
            await field.AddTagAsync("新規");
            Assert.That(field.Tags, Is.Empty);
            Assert.That(field.IsValid, Is.False);
            await field.AddTagAsync("展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会" }));
            Assert.That(field.IsValid, Is.True);
        }

        [Test]
        public async Task 候補はマスタを名前順に1回だけ読む()
        {
            var (services, field) = await CreatePickerAsync(e => e.CandidateRowCount = 300);
            Assert.That(await field.GetCandidatesAsync(), Is.EqualTo(new[] { "展示会", "DXPO" }));
            await field.GetCandidatesAsync();
            var request = services.App.ListRequests.Single();
            Assert.That(request.Condition.ModuleName, Is.EqualTo("Tag"));
            Assert.That(request.Condition.LimitCount, Is.EqualTo(300));
            Assert.That(request.Condition.SortConditions.Single().Variable, Is.EqualTo("Name.Value"));
            Assert.That(request.Condition.SelectFields, Is.EquivalentTo(new[] { "Id", "Name" }));
        }

        [Test]
        public async Task SetTagsは外したタグを外し無いタグを足す()
        {
            var (_, field) = await CreatePickerAsync();
            await field.SetTagsAsync(["展示会", "DXPO"]);
            await field.SetTagsAsync(["dxpo", "セミナー"]);
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "セミナー" }));
        }

        [Test]
        public async Task 必須の検証()
        {
            var (_, field) = await CreatePickerAsync(e => e.IsRequired = true);
            Assert.That(await field.ValidateInput(), Is.False);
            await field.AddTagAsync("展示会");
            Assert.That(await field.ValidateInput(), Is.True);
        }

        [Test]
        public void タグの区切りは読点と全角カンマも使え前後の空白と重複を落とす()
            => Assert.That(TagField.Normalize([" 展示会 ,DXPO、セミナー，展示会,, ", "dxpo"]), Is.EqualTo(new[] { "展示会", "DXPO", "セミナー" }));

        #endregion

        #region デザインチェック

        [Test]
        public void デザインチェック_セットアップの形なら指摘なし()
        {
            var d = TagTestDesigns.Create();
            Assert.That(Check(d, "Contact", "Tags"), Is.Empty);
            Assert.That(Check(d, "Picker", "Pick"), Is.Empty);
            Assert.That(Check(d, "Tag", "TagContract"), Is.Empty);
            Assert.That(Check(d, "ContactTags", "TagLinkContract"), Is.Empty);
        }

        [Test]
        public void デザインチェック_入力欄はマスタが必須でマスタは契約を持つこと()
        {
            var d = TagTestDesigns.Create(customizePicker: e => e.TagModuleName = string.Empty);
            var ret = Check(d, "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Picker", "Pick", "TagModuleName");

            d = TagTestDesigns.Create(customizePicker: e => e.TagModuleName = "Contact");
            ret = Check(d, "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Picker", "Pick", "TagModuleName");
        }

        [Test]
        public void デザインチェック_検索条件のモジュールはタグ付けモジュールであること()
        {
            var d = TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition { ModuleName = "Tag" });
            var ret = Check(d, "Contact", "Tags");
            Assert.That(ret.Select(e => ((FieldDesignCheckInfo)e).Location.Member), Does.Contain("SearchCondition"));
        }

        [Test]
        public void デザインチェック_このレコードへの結び付きが無ければ指摘()
        {
            var d = TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition { ModuleName = "ContactTags" });
            var ret = Check(d, "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Contact", "Tags", "SearchCondition");
            Assert.That(ret[0].Message, Does.Contain("OwnerId.Value"));
        }

        [Test]
        public void デザインチェック_タグ付けの一覧がOwnerIdとTagを読まなければ指摘()
        {
            var d = TagTestDesigns.Create();
            d.Modules.Find("ContactTags")!.ListLayouts[""].DataOnlyFields.Remove("Tag");
            var ret = Check(d, "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain("Tag"));
        }

        [Test]
        public void デザインチェック_マスタを書くならタグ付けのリンクの先と同じ()
        {
            Assert.That(Check(TagTestDesigns.Create(e => e.TagModuleName = "Tag"), "Contact", "Tags"), Is.Empty);
            var ret = Check(TagTestDesigns.Create(e => e.TagModuleName = "Picker"), "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Contact", "Tags", "TagModuleName");
        }

        [Test]
        public void デザインチェック_候補の行数は1以上()
        {
            var ret = Check(TagTestDesigns.Create(e => e.CandidateRowCount = 0), "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Contact", "Tags", "CandidateRowCount");
        }

        [Test]
        public void デザインチェック_契約の役割の型とリンクの先()
        {
            var d = TagTestDesigns.Create();
            var link = d.Modules.Find("ContactTags")!;
            ((LinkFieldDesign)link.Fields.Single(e => e.Name == "Tag")).SearchCondition = new SearchCondition { ModuleName = "Contact" };
            var ret = Check(d, "ContactTags", "TagLinkContract");
            Assert.That(ret, Has.Count.EqualTo(1), string.Join(" | ", ret.Select(e => e.Message)));
            ret[0].AssertFieldLocation("ContactTags", "TagLinkContract", "Tag");

            d = TagTestDesigns.Create();
            ((LinkFieldDesign)d.Modules.Find("ContactTags")!.Fields.Single(e => e.Name == "Tag")).DisplayTextVariable = "Id.Value";
            ret = Check(d, "ContactTags", "TagLinkContract");
            Assert.That(ret, Has.Count.EqualTo(1), "リンクの表示文字列はタグ名");
            Assert.That(ret[0].Message, Does.Contain("Name.Value"));

            d = TagTestDesigns.Create();
            var contract = d.Modules.Find("ContactTags")!.Fields.OfType<TagLinkContractFieldDesign>().Single();
            contract.OwnerId = "Tag";
            ret = Check(d, "ContactTags", "TagLinkContract");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("ContactTags", "TagLinkContract", "OwnerId");

            d = TagTestDesigns.Create();
            d.Modules.Find("Tag")!.Fields.OfType<TagContractFieldDesign>().Single().TagName = "Id";
            ret = Check(d, "Tag", "TagContract");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Tag", "TagContract", "TagName");
        }

        #endregion

        [Test]
        public void マスタのモジュール名の変更に追従する()
        {
            var d = TagTestDesigns.Create();
            var pick = (TagFieldDesign)d.Modules.Find("Picker")!.Fields.Single();
            var result = pick.ChangeName(new RenameContext(d) { Type = RenameType.Module, Source = "Tag", Destination = "Label" });
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(pick.TagModuleName, Is.EqualTo("Label"));
        }
    }
}
