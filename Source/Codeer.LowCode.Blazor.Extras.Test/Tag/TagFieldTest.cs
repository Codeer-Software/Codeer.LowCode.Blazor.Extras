using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>TagField のランタイム (値・タグの操作・検証・検索条件・候補) とデザインチェック。</summary>
    public class TagFieldTest
    {
        static async Task<(TestServices Services, TagField Field)> CreateAsync(Action<TagFieldDesign>? customize = null)
        {
            var d = new DesignData();
            var mod = new ModuleDesign { Name = "Contact", DataSourceName = "Main", DbTable = "contacts" };
            mod.Fields.Add(new IdFieldDesign { Name = "Id" });
            var tag = new TagFieldDesign { Name = "Tags", DbColumn = "tags" };
            customize?.Invoke(tag);
            mod.Fields.Add(tag);
            d.AddModule(mod);
            var services = new TestServices(d);
            var module = await services.CreateModuleAsync("Contact");
            return (services, (TagField)module.GetField("Tags")!);
        }

        static ModuleData Row(string tags)
        {
            var row = new ModuleData { Name = "Contact" };
            row.Fields["Tags"] = new TagFieldData { Value = tags };
            return row;
        }

        [Test]
        public async Task 値はタグをカンマ区切りで持ちTagsは区切ったもの()
        {
            var (_, field) = await CreateAsync();
            await field.SetValueAsync(" 展示会 ,DXPO2027、セミナー，展示会,, ");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "DXPO2027", "セミナー" }));

            await field.SetTagsAsync(field.Tags);
            Assert.That(field.Value, Is.EqualTo("展示会, DXPO2027, セミナー"));
        }

        [Test]
        public async Task AddTagは末尾に足し同じタグは重ねない()
        {
            var (_, field) = await CreateAsync();
            await field.AddTagAsync("展示会");
            await field.AddTagAsync("DXPO2027");
            await field.AddTagAsync(" 展示会 ");
            Assert.That(field.Value, Is.EqualTo("展示会, DXPO2027"));
            Assert.That(field.IsModified, Is.True);
        }

        [Test]
        public async Task 大文字小文字は区別せず先の表記を残す()
        {
            var (_, field) = await CreateAsync();
            await field.AddTagAsync("DXPO");
            await field.AddTagAsync("dxpo");
            Assert.That(field.Value, Is.EqualTo("DXPO"));
            Assert.That(field.HasTag("Dxpo"), Is.True);

            await field.SetValueAsync("展示会, Expo, expo, EXPO");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会", "Expo" }));

            await field.RemoveTagAsync("EXPO");
            Assert.That(field.Tags, Is.EqualTo(new[] { "展示会" }));
        }

        [Test]
        public async Task RemoveTagとHasTag_タグが無くなれば値は空文字()
        {
            var (_, field) = await CreateAsync();
            await field.SetValueAsync("展示会, DXPO2027");
            Assert.That(field.HasTag("DXPO2027"), Is.True);

            await field.RemoveTagAsync("DXPO2027");
            Assert.That(field.HasTag("DXPO2027"), Is.False);
            await field.RemoveTagAsync("展示会");
            Assert.That(field.Value, Is.EqualTo(string.Empty));
            Assert.That(field.Tags, Is.Empty);
        }

        [Test]
        public async Task TextEditEmptyTypeがNullならタグが無くなれば値はnull()
        {
            var (_, field) = await CreateAsync(d => d.TextEditEmptyType = TextEditEmptyType.Null);
            await field.AddTagAsync("展示会");
            await field.RemoveTagAsync("展示会");
            Assert.That(field.Value, Is.Null);
        }

        [Test]
        public async Task 必須の検証()
        {
            var (_, field) = await CreateAsync(d => d.IsRequired = true);
            Assert.That(await field.ValidateInput(), Is.False);
            Assert.That(field.IsValid, Is.False);

            await field.AddTagAsync("展示会");
            Assert.That(await field.ValidateInput(), Is.True);
        }

        [Test]
        public async Task 検索条件は選んだタグごとのLikeをすべて含むでまとめる()
        {
            var (_, field) = await CreateAsync();
            Assert.That(field.GetMatchCondition(), Is.Null);

            await field.SetSearchTagsAsync(["展示会", "DXPO2027"]);
            var condition = (FieldMatchCondition)field.GetMatchCondition()!;
            Assert.That(condition.FieldName, Is.EqualTo("Tags"));
            Assert.That(condition.IsOrMatch, Is.False);
            var children = condition.Children.Cast<FieldValueMatchCondition>().ToList();
            Assert.That(children.Select(e => e.SearchTargetVariable), Is.All.EqualTo("Tags.Value"));
            Assert.That(children.Select(e => e.Comparison), Is.All.EqualTo(MatchComparison.Like));
            Assert.That(children.Select(e => ((StringValue)e.Value).Value), Is.EqualTo(new[] { "展示会", "DXPO2027" }));
        }

        [Test]
        public async Task いずれかを含むはOR_既定はデザインで画面から切り替えられる()
        {
            var (_, field) = await CreateAsync(d => d.SearchMatchDefaultValue = TagSearchMatch.Any);
            await field.SetSearchTagsAsync(["展示会"]);
            Assert.That(((FieldMatchCondition)field.GetMatchCondition()!).IsOrMatch, Is.True);

            await field.SetSearchMatchAsync(TagSearchMatch.All);
            Assert.That(((FieldMatchCondition)field.GetMatchCondition()!).IsOrMatch, Is.False);
        }

        [Test]
        public async Task タグを選んでいなければ一致に関係なく絞らない()
        {
            var (_, field) = await CreateAsync(d => d.SearchMatchDefaultValue = TagSearchMatch.Any);
            Assert.That(field.GetMatchCondition(), Is.Null);

            await field.SetSearchTagsAsync(["展示会"]);
            await field.SetSearchTagsAsync([]);
            Assert.That(field.GetMatchCondition(), Is.Null);
        }

        [Test]
        public async Task SetMatchConditionで条件から戻せる_ClearMatchConditionで空になる()
        {
            var (_, field) = await CreateAsync();
            await field.SetSearchTagsAsync(["展示会", "DXPO2027"]);
            await field.SetSearchMatchAsync(TagSearchMatch.Any);
            var saved = (FieldMatchCondition)field.GetMatchCondition()!;

            await field.ClearMatchConditionAsync();
            Assert.That(field.GetMatchCondition(), Is.Null);
            Assert.That(field.SearchMatch, Is.EqualTo(TagSearchMatch.All));

            await field.SetMatchConditionAsync(saved);
            Assert.That(field.SearchTags, Is.EqualTo(new[] { "展示会", "DXPO2027" }));
            Assert.That(field.SearchMatch, Is.EqualTo(TagSearchMatch.Any));
        }

        [Test]
        public async Task 検索の値が変わると画面へ知らせる()
        {
            var (_, field) = await CreateAsync();
            var count = 0;
            field.OnSearchDataChangedAsync = () => { count++; return Task.CompletedTask; };
            await field.SetSearchTagsAsync(["展示会"]);
            await field.SetSearchMatchAsync(TagSearchMatch.Any);
            await field.ClearMatchConditionAsync();
            Assert.That(count, Is.EqualTo(3));
        }

        [Test]
        public async Task 候補はタグの列だけを1回読み多く付いている順()
        {
            var (services, field) = await CreateAsync();
            services.App.ListProvider = _ => new Paging<ModuleData> { Items = [Row("展示会, DXPO2027"), Row("展示会"), Row("セミナー, 展示会")] };
            Assert.That(await field.GetCandidatesAsync(), Is.EqualTo(new[] { "展示会", "DXPO2027", "セミナー" }));

            var request = services.App.ListRequests.Single();
            Assert.That(request.Condition.ModuleName, Is.EqualTo("Contact"));
            Assert.That(request.Condition.SelectFields, Does.Contain("Tags"));
            Assert.That(request.Condition.LimitCount, Is.GreaterThan(0));

            await field.GetCandidatesAsync();
            Assert.That(services.App.ListRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task テーブルを持たない画面では指定したモジュールのタグを候補にする()
        {
            var d = new DesignData();
            var contact = new ModuleDesign { Name = "Contact", DataSourceName = "Main", DbTable = "contacts" };
            contact.Fields.Add(new IdFieldDesign { Name = "Id" });
            contact.Fields.Add(new TagFieldDesign { Name = "Tags", DbColumn = "tags" });
            d.AddModule(contact);
            var import = new ModuleDesign { Name = "Import" };
            import.Fields.Add(new TagFieldDesign { Name = "NewTags", CandidateModuleName = "Contact", CandidateFieldName = "Tags" });
            import.Fields.Add(new TagFieldDesign { Name = "Plain" });
            d.AddModule(import);
            var services = new TestServices(d);
            var module = await services.CreateModuleAsync("Import");
            services.App.ListProvider = _ => new Paging<ModuleData> { Items = [Row("展示会")] };

            Assert.That(await ((TagField)module.GetField("NewTags")!).GetCandidatesAsync(), Is.EqualTo(new[] { "展示会" }));
            Assert.That(services.App.ListRequests.Single().Condition.ModuleName, Is.EqualTo("Contact"));

            //指定が無ければ自分のモジュール (テーブルが無いので問い合わせない)
            Assert.That(await ((TagField)module.GetField("Plain")!).GetCandidatesAsync(), Is.Empty);
            Assert.That(services.App.ListRequests, Has.Count.EqualTo(1));
        }

        [Test]
        public void デザインチェック_DB列があれば指摘なし()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new TagFieldDesign { Name = "Tags", DbColumn = "DbColumn" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void デザインチェック_存在しないDB列は指摘()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var field = new TagFieldDesign { Name = "Tags", DbColumn = "NoSuchColumn" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("mod", "Tags", "DbColumn");
        }

        [Test]
        public void デザインチェック_存在しない候補のモジュールとフィールドは指摘()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var noModule = new TagFieldDesign { Name = "Tags", DbColumn = "DbColumn", CandidateModuleName = "NoSuchModule" };
            module.Fields.Add(noModule);
            var ret = noModule.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.GreaterThanOrEqualTo(1));
            ret[0].AssertFieldLocation("mod", "Tags", "CandidateModuleName");

            var noField = new TagFieldDesign { Name = "Tags2", DbColumn = "DbColumn", CandidateModuleName = "mod", CandidateFieldName = "NoSuchField" };
            module.Fields.Add(noField);
            ret = noField.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("mod", "Tags2", "CandidateFieldName");
        }

        [Test]
        public void 検索条件の検証は本体TextFieldと同じ規則()
        {
            var tag = new TagFieldDesign { Name = "Tags", DbColumn = "tags" };
            var text = new TextFieldDesign { Name = "Tags", DbColumn = "tags" };
            var conditions = new List<FieldValueMatchCondition>();
            foreach (var comparison in Enum.GetValues<MatchComparison>())
            {
                conditions.Add(new FieldValueMatchCondition { SearchTargetVariable = "Tags.Value", Comparison = comparison, Value = new StringValue { Value = "展示会" } });
                conditions.Add(new FieldValueMatchCondition { SearchTargetVariable = "Tags.Value", Comparison = comparison, Value = new StringValue { Value = string.Empty } });
                conditions.Add(new FieldValueMatchCondition { SearchTargetVariable = "Tags.Value", Comparison = comparison, Value = new NullValue() });
            }
            foreach (var allowEmpty in new[] { false, true })
            {
                tag.AllowEmptySearch = allowEmpty;
                text.AllowEmptySearch = allowEmpty;
                foreach (var condition in conditions)
                {
                    Assert.That(string.IsNullOrEmpty(tag.ValidateSearchCondition(condition)), Is.EqualTo(string.IsNullOrEmpty(text.ValidateSearchCondition(condition))),
                        $"AllowEmptySearch={allowEmpty} {condition.Comparison} {JsonConverterEx.SerializeObject(condition.Value)}");
                }
            }
        }
    }
}
