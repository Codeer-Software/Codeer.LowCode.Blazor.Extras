using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>TagField / TagInputField の DB を使わない部分: 保存しない入力欄、候補の出どころ、タグの区切り、デザインチェック、リネーム追従。</summary>
    public class TagFieldTest
    {
        static readonly Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>> NoTables = new();

        //集計の結果 (名前と件数。件数の多い順に並べて渡す)
        static AggregateResult Counts(params (string Name, int Count)[] rows) => new()
        {
            Rows = rows.Select(e => new AggregateRow
            {
                Keys = [new AggregateKey { Value = new StringValue { Value = e.Name }, DisplayText = e.Name }],
                Values = [new DecimalValue { Value = e.Count }],
            }).ToList(),
        };

        //候補の集計はハーネスが返す (展示会 2 / DXPO 1)
        static async Task<(TestServices Services, TagInputField Field)> CreatePickerAsync(Action<TagInputFieldDesign>? customize = null)
        {
            var services = new TestServices(TagTestDesigns.Create(customizePicker: customize));
            services.App.AggregateProvider = conditions => conditions.Select(_ => Counts(("展示会", 2), ("DXPO", 1))).ToList();
            var module = await services.CreateModuleAsync("Picker", ModuleLayoutType.Detail);
            return (services, (TagInputField)module.GetField("Pick")!);
        }

        static List<DesignCheckInfo> Check(DesignData d, string module, string field)
            => d.Modules.Find(module)!.Fields.Single(e => e.Name == field).CheckDesign(new DesignCheckContext(module, d, NoTables));

        #region 保存しない入力欄 (TagInputField)

        [Test]
        public async Task 入力欄はタグ名を持つだけで保存しない()
        {
            var (services, field) = await CreatePickerAsync();
            await field.AddTagAsync("dxpo");
            Assert.That(services.App.AggregateRequests, Is.Empty, "新しいタグを入れてよい入力欄は、足すだけでは問い合わせない (保存する TagField の側で表記を寄せる)");
            Assert.That(field.Tags, Is.EqualTo(new[] { "dxpo" }));
            await field.RemoveTagAsync("DXPO");

            await field.GetCandidatesAsync("x");
            await field.AddTagAsync("dxpo、新規 ,展示会");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "新規", "展示会" }), "候補を引いていれば使われている表記");
            Assert.That(field.IsModified, Is.True);
            Assert.That(field.GetData(), Is.Null);
            Assert.That(field.GetSubmitData().Add, Is.Empty);
            Assert.That(services.App.ListRequests, Is.Empty, "行は読まない");

            await field.RemoveTagAsync("新規");
            Assert.That(field.HasTag("展示会"), Is.True);
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "展示会" }));
            await field.ClearAsync();
            Assert.That(field.Tags, Is.Empty);
        }

        [Test]
        public async Task 候補はタグ付けのタグ名を件数の多い順に打った文字で絞って引く()
        {
            var (services, field) = await CreatePickerAsync();
            Assert.That(await field.GetCandidatesAsync(" 展 "), Is.EqualTo(new[] { "展示会", "DXPO" }));
            var condition = services.App.AggregateRequests.Single().Single();
            Assert.That(condition.ModuleName, Is.EqualTo("ContactTags"), "候補の列が TagField なら、そのタグ付けモジュール");
            Assert.That(condition.Groups.Single().Variable, Is.EqualTo("Name.Value"));
            Assert.That(condition.Measures.Single().Function, Is.EqualTo(AggregateFunction.Count));
            Assert.That(condition.SortConditions[0].Target, Is.EqualTo(AggregateSortTarget.Measure));
            Assert.That(condition.SortConditions[0].IsDescending, Is.True);
            Assert.That(condition.LimitCount, Is.EqualTo(20));
            var like = (FieldValueMatchCondition)((MultiMatchCondition)condition.Condition).Children.Single();
            Assert.That(like.SearchTargetVariable, Is.EqualTo("Name.Value"));
            Assert.That(like.Comparison, Is.EqualTo(MatchComparison.Like));
            Assert.That(((StringValue)like.Value!).Value, Is.EqualTo("展"));
        }

        [Test]
        public async Task 入力欄でもAllowNewTagsがfalseなら候補のタグだけ()
        {
            var (services, field) = await CreatePickerAsync(e => e.AllowNewTags = false);
            await field.AddTagAsync("新規");
            Assert.That(field.Tags, Is.Empty);
            Assert.That(field.IsValid, Is.False);
            await field.AddTagAsync("dxpo");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO" }), "表記は使われているもの");
            Assert.That(field.IsValid, Is.True);
            var equal = (FieldValueMatchCondition)((MultiMatchCondition)services.App.AggregateRequests.Last().Single().Condition).Children.Single();
            Assert.That(equal.Comparison, Is.EqualTo(MatchComparison.Equal), "あるかどうかは名前の一致で 1 回引く");
        }

        [Test]
        public async Task 決まったタグは問い合わせずに出し決まったタグだけにできる()
        {
            var (services, field) = await CreatePickerAsync(e =>
            {
                e.CandidateSource = TagCandidateSource.Values;
                e.CandidateValues = "高\r\n中\n 低 \n\n中";
                e.AllowNewTags = false;
            });
            Assert.That(await field.GetCandidatesAsync(""), Is.EqualTo(new[] { "高", "中", "低" }), "並びのまま・空行と重複を落とす");
            Assert.That(await field.GetCandidatesAsync("低"), Is.EqualTo(new[] { "低" }));
            await field.AddTagAsync("最低");
            await field.AddTagAsync("中");
            Assert.That(field.Tags, Is.EqualTo(new[] { "中" }));
            Assert.That(services.App.AggregateRequests, Is.Empty);
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
            Assert.That(Check(d, "ContactTags", "TagLinkContract"), Is.Empty);
        }

        [Test]
        public void デザインチェック_検索条件のモジュールはタグ付けモジュールであること()
        {
            var ret = Check(TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition { ModuleName = "Contact" }), "Contact", "Tags");
            Assert.That(ret.Select(e => ((FieldDesignCheckInfo)e).Location.Member), Does.Contain("SearchCondition"));
            ret = Check(TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition()), "Contact", "Tags");
            Assert.That(ret.Select(e => ((FieldDesignCheckInfo)e).Location.Member), Does.Contain("SearchCondition"), "空も指摘 (保存しない欄は TagInputField)");
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
        public void デザインチェック_タグ付けの一覧がOwnerIdとタグ名を読まなければ指摘()
        {
            var d = TagTestDesigns.Create();
            d.Modules.Find("ContactTags")!.ListLayouts[""].DataOnlyFields.Remove("Name");
            var ret = Check(d, "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain("Name"));
        }

        [Test]
        public void デザインチェック_候補の設定()
        {
            var ret = Check(TagTestDesigns.Create(customizePicker: e => e.CandidateModuleName = string.Empty), "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Picker", "Pick", "CandidateModuleName");

            ret = Check(TagTestDesigns.Create(customizePicker: e => e.CandidateFieldName = "Id"), "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1), "列は TextField か TagField");
            ret[0].AssertFieldLocation("Picker", "Pick", "CandidateFieldName");

            Assert.That(Check(TagTestDesigns.Create(customizePicker: e => { e.CandidateModuleName = "ContactTags"; e.CandidateFieldName = "Name"; }), "Picker", "Pick"), Is.Empty, "TextField も可");

            ret = Check(TagTestDesigns.Create(customizePicker: e => e.CandidateSource = TagCandidateSource.Values), "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Picker", "Pick", "CandidateValues");

            ret = Check(TagTestDesigns.Create(customizePicker: e => e.CandidateSource = TagCandidateSource.TagRows), "Picker", "Pick");
            Assert.That(ret, Has.Count.EqualTo(1), "入力欄は自分のタグ付けを持たない");
            ret[0].AssertFieldLocation("Picker", "Pick", "CandidateSource");

            ret = Check(TagTestDesigns.Create(e => e.CandidateSource = TagCandidateSource.Module), "Contact", "Tags");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("Contact", "Tags", "CandidateModuleName");
        }

        [Test]
        public void デザインチェック_契約の役割の型()
        {
            var d = TagTestDesigns.Create();
            d.Modules.Find("ContactTags")!.Fields.OfType<TagLinkContractFieldDesign>().Single().OwnerId = "Name";
            var ret = Check(d, "ContactTags", "TagLinkContract");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("ContactTags", "TagLinkContract", "OwnerId");

            d = TagTestDesigns.Create();
            d.Modules.Find("ContactTags")!.Fields.OfType<TagLinkContractFieldDesign>().Single().TagName = "OwnerId";
            ret = Check(d, "ContactTags", "TagLinkContract");
            Assert.That(ret, Has.Count.EqualTo(1));
            ret[0].AssertFieldLocation("ContactTags", "TagLinkContract", "TagName");
        }

        #endregion

        [Test]
        public void 候補のモジュール名の変更に追従する()
        {
            var d = TagTestDesigns.Create();
            var pick = (TagInputFieldDesign)d.Modules.Find("Picker")!.Fields.Single();
            var result = pick.ChangeName(new RenameContext(d) { Type = RenameType.Module, Source = "Contact", Destination = "Person" });
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(pick.CandidateModuleName, Is.EqualTo("Person"));
        }
    }
}
