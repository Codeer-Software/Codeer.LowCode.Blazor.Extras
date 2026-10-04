using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.Test.CrossTab
{
    // 実行時の経路: 追加の条件 (一覧の検索条件・スクリプト) は表の条件と AND / 失敗は LoadError / カスタマイズできる表は保存内容を受け取るまで集計しない
    public class CrossTabFieldRuntimeTest
    {
        static DesignData Design(bool canCustomize = false)
        {
            var d = new DesignData();
            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            order.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "status" });
            order.Fields.Add(new TextFieldDesign { Name = "Owner", DbColumn = "owner" });
            d.AddModule(order);

            var other = new ModuleDesign { Name = "Other", DataSourceName = "Main", DbTable = "others" };
            other.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            d.AddModule(other);

            var summary = new ModuleDesign { Name = "Summary" };
            var tab = new CrossTabFieldDesign { Name = "Tab", CanCustomize = canCustomize };
            tab.SearchCondition.ModuleName = "Order";
            tab.SearchCondition.Condition = Equal("Owner.Value", "A");
            tab.Setting.Rows.Add(new ValueGroup { Variable = "Status.Value" });
            tab.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            summary.Fields.Add(tab);
            //画面に置いた表 (置いていない項目は読み込みで集計しない)
            summary.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Tab" } };
            d.AddModule(summary);
            return d;
        }

        static FieldValueMatchCondition Equal(string variable, object value)
            => new() { SearchTargetVariable = variable, Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(value) };

        static AggregateRow Row(decimal count, params object?[] keys)
            => new() { Keys = keys.Select(k => new AggregateKey { Value = MultiTypeValue.Create(k) }).ToList(), Values = [MultiTypeValue.Create(count)] };

        static List<AggregateResult> Results(List<AggregateCondition> conditions)
            => conditions.Select(c => c.Groups.Count == 0
                ? new AggregateResult { Rows = [Row(3)], TotalCount = 3, GroupCount = 1 }
                : new AggregateResult { Rows = [Row(2, "10"), Row(1, "20")], TotalCount = 3, GroupCount = 2 }).ToList();

        //条件の葉 (変数 = 値) を全部
        static List<string> Leaves(MatchConditionBase? c) => c switch
        {
            null => [],
            MultiMatchCondition m => m.Children.SelectMany(Leaves).ToList(),
            FieldValueMatchCondition v => [$"{v.SearchTargetVariable}={v.Value.GetValue()}"],
            _ => [c.GetType().Name],
        };

        static async Task<(TestServices Svc, CrossTabField Field)> CreateAsync(DesignData design, ModuleLayoutType layoutType = ModuleLayoutType.None)
        {
            var svc = new TestServices(design);
            svc.App.AggregateProvider = Results;
            var module = await svc.CreateModuleAsync("Summary", layoutType);
            return (svc, module.GetField<CrossTabField>("Tab")!);
        }

        [Test]
        public async Task 追加の条件は表の条件とANDになり送る定義の全部に入る()
        {
            var (svc, field) = await CreateAsync(Design());
            var searcher = new ModuleSearcher("Order");
            searcher.AddConditions(Equal("Status.Value", "10"));
            await field.SetAdditionalConditionAsync(searcher);

            Assert.That(field.Table, Is.Not.Null, field.LoadError);
            var sent = svc.App.AggregateRequests.Last();
            //本体と総計の両方に、設計の条件と追加の条件が入る
            Assert.That(sent, Has.Count.EqualTo(2));
            foreach (var condition in sent)
                Assert.That(Leaves(condition.Condition), Is.EquivalentTo(new[] { "Owner.Value=A", "Status.Value=10" }));
        }

        [Test]
        public async Task スクリプトのShowで渡した定義にも追加の条件がANDで入り設計の条件は使わない()
        {
            var (svc, field) = await CreateAsync(Design());
            var searcher = new ModuleSearcher("Order");
            searcher.AddConditions(Equal("Status.Value", "10"));
            await field.SetAdditionalConditionAsync(searcher);

            var aggregatorWhere = new ModuleSearcher("Order");
            aggregatorWhere.AddConditions(Equal("Owner.Value", "B"));
            var aggregator = new ModuleAggregator("Order");
            aggregator.Where(aggregatorWhere);
            aggregator.GroupBy((Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax)Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression("m => m.Status"));
            aggregator.Count();
            await field.ShowAsync(aggregator, 1);

            Assert.That(field.Table, Is.Not.Null, field.LoadError);
            foreach (var condition in svc.App.AggregateRequests.Last())
                Assert.That(Leaves(condition.Condition), Is.EquivalentTo(new[] { "Owner.Value=B", "Status.Value=10" }));
        }

        [Test]
        public async Task 別のモジュールの追加の条件は拒否する()
        {
            var (_, field) = await CreateAsync(Design());
            Assert.That(() => field.SetAdditionalConditionAsync(new ModuleSearcher("Other")), Throws.TypeOf<LowCodeException>());
        }

        [Test]
        public async Task 集計が失敗したら表を消してLoadErrorに文言を入れ成功したら消す()
        {
            var (svc, field) = await CreateAsync(Design());
            await field.ReloadAsync();
            Assert.That(field.Table, Is.Not.Null);

            svc.App.AggregateProvider = _ => throw new LowCodeException("集計できません");
            await field.ReloadAsync();
            Assert.Multiple(() =>
            {
                Assert.That(field.Table, Is.Null);
                Assert.That(field.LoadError, Is.EqualTo("集計できません"));
                Assert.That(field.IsLoading, Is.False);
            });
            //失敗した表のセルからは明細の条件を作らない
            Assert.That(field.CreateCellCondition(0, null), Is.Null);

            svc.App.AggregateProvider = Results;
            await field.ReloadAsync();
            Assert.That(field.LoadError, Is.Empty);
            Assert.That(field.Table, Is.Not.Null);
        }

        [Test]
        public async Task カスタマイズできる表は画面が保存内容を渡すまで集計せず渡したら1回だけ集計する()
        {
            //カスタマイズできない表は読み込みで集計する (比較用)
            var (plainSvc, plain) = await CreateAsync(Design(canCustomize: false), ModuleLayoutType.Detail);
            Assert.That(plainSvc.App.AggregateRequests, Has.Count.EqualTo(1));
            Assert.That(plain.Table, Is.Not.Null);

            var (svc, field) = await CreateAsync(Design(canCustomize: true), ModuleLayoutType.Detail);
            Assert.That(svc.App.AggregateRequests, Is.Empty);
            Assert.That(field.Table, Is.Null);

            //保存内容なし (設計どおり) を渡すと 1 回集計する
            await field.ApplyUserSettingAsync(null, null);
            Assert.That(svc.App.AggregateRequests, Has.Count.EqualTo(1));
            Assert.That(field.Table, Is.Not.Null);
        }
    }
}
