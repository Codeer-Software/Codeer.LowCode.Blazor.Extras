using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>TagField の DB を使わない部分: 候補の問い合わせの形、タグの区切り・同一判定・長さ、デザインチェック。</summary>
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

        //新規の Contact の TagField (タグ付け行の読み込みはハーネスが空で返す。候補の集計は 展示会 2 / DXPO 1)
        static async Task<(TestServices Services, TagField Field)> CreateAsync(Action<TagFieldDesign>? customize = null)
        {
            var services = new TestServices(TagTestDesigns.Create(customize));
            services.App.AggregateProvider = conditions => conditions.Select(_ => Counts(("展示会", 2), ("DXPO", 1))).ToList();
            var module = await services.CreateModuleAsync("Contact", ModuleLayoutType.Detail);
            return (services, (TagField)module.GetField("Tags")!);
        }

        static List<DesignCheckInfo> Check(DesignData d, string module, string field)
            => d.Modules.Find(module)!.Fields.Single(e => e.Name == field).CheckDesign(new DesignCheckContext(module, d, NoTables));

        [Test]
        public async Task 候補はタグ付けのタグ名を件数の多い順に打った文字で絞って10件引く()
        {
            var (services, field) = await CreateAsync();
            Assert.That(await field.GetCandidatesAsync(" 展 "), Is.EqualTo(new[] { "展示会", "DXPO" }));
            var condition = services.App.AggregateRequests.Single().Single();
            Assert.That(condition.ModuleName, Is.EqualTo("ContactTags"), "自分のタグ付けモジュール");
            Assert.That(condition.Groups.Single().Variable, Is.EqualTo("Name.Value"));
            Assert.That(condition.Measures.Single().Function, Is.EqualTo(AggregateFunction.Count));
            Assert.That(condition.SortConditions[0].Target, Is.EqualTo(AggregateSortTarget.Measure));
            Assert.That(condition.SortConditions[0].IsDescending, Is.True);
            Assert.That(condition.SortConditions[1].Target, Is.EqualTo(AggregateSortTarget.Group), "同じ件数は名前順");
            Assert.That(condition.LimitCount, Is.EqualTo(10));
            var like = (FieldValueMatchCondition)((MultiMatchCondition)condition.Condition).Children.Single();
            Assert.That(like.SearchTargetVariable, Is.EqualTo("Name.Value"));
            Assert.That(like.Comparison, Is.EqualTo(MatchComparison.Like));
            Assert.That(((StringValue)like.Value!).Value, Is.EqualTo("展"));

            Assert.That(await field.GetCandidatesAsync("  "), Is.Empty, "何も打っていなければ問い合わせない");
            Assert.That(services.App.AggregateRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task タグを足しても問い合わせない()
        {
            var (services, field) = await CreateAsync();
            await field.AddTagAsync("展示会, dxpo");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "dxpo" }), "打ったまま (表記を寄せない)");
            Assert.That(services.App.AggregateRequests, Is.Empty);
        }

        [Test]
        public async Task 同一判定は完全一致()
        {
            var (_, field) = await CreateAsync();
            await field.AddTagAsync("DXPO");
            await field.AddTagAsync("dxpo");
            await field.AddTagAsync(" DXPO ");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "dxpo" }));
            Assert.That(field.HasTag("DXPO"), Is.True);
            Assert.That(field.HasTag("Dxpo"), Is.False);
            await field.RemoveTagAsync("dxpo");
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO" }));
            await field.SetTagsAsync(["dxpo", "DXPO"]);
            Assert.That(field.Tags, Is.EqualTo(new[] { "DXPO", "dxpo" }), "置き換え: 付いている DXPO は残り、dxpo が足される");
        }

        [Test]
        public async Task タグ名は200文字まで()
        {
            var (_, field) = await CreateAsync();
            await field.AddTagAsync(new string('あ', TagField.MaxTagLength));
            Assert.That(field.Tags.Single(), Has.Length.EqualTo(200));
            Assert.That(field.IsValid, Is.True);

            await field.AddTagAsync(new string('い', TagField.MaxTagLength + 1));
            Assert.That(field.Tags, Has.Count.EqualTo(1), "201 文字は足さない");
            Assert.That(field.IsValid, Is.False);
            Assert.That(field.ErrorText, Does.Contain("200"));

            await field.AddTagAsync("短い");
            Assert.That(field.IsValid, Is.True, "足せたらエラーは消える");
        }

        [Test]
        public async Task 必須の検証()
        {
            var (_, field) = await CreateAsync(e => e.IsRequired = true);
            Assert.That(await field.ValidateInput(), Is.False);
            await field.AddTagAsync("展示会");
            Assert.That(await field.ValidateInput(), Is.True);
        }

        [Test]
        public void タグの区切りは読点と全角カンマも使え前後の空白と完全一致の重複を落とす()
            => Assert.That(TagField.Normalize([" 展示会 ,DXPO、セミナー，展示会,, ", "dxpo"]), Is.EqualTo(new[] { "展示会", "DXPO", "セミナー", "dxpo" }));

        #region デザインチェック

        [Test]
        public void デザインチェック_セットアップの形なら指摘なし()
        {
            var d = TagTestDesigns.Create();
            Assert.That(Check(d, "Contact", "Tags"), Is.Empty);
            Assert.That(Check(d, "ContactTags", "TagLinkContract"), Is.Empty);
        }

        [Test]
        public void デザインチェック_検索条件のモジュールはタグ付けモジュールであること()
        {
            var ret = Check(TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition { ModuleName = "Contact" }), "Contact", "Tags");
            Assert.That(ret.Select(e => ((FieldDesignCheckInfo)e).Location.Member), Does.Contain("SearchCondition"));
            ret = Check(TagTestDesigns.Create(e => e.SearchCondition = new SearchCondition()), "Contact", "Tags");
            Assert.That(ret.Select(e => ((FieldDesignCheckInfo)e).Location.Member), Does.Contain("SearchCondition"), "空も指摘");
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
    }
}
