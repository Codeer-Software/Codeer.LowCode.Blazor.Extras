using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.CrossTab
{
    // セルの条件 (明細へ): 表の条件 + そのときの行・列の項目の鍵 (日付は期間・空値は空値の行)。行を上限で選んだ表の合計は表に出ている行の分
    public class CrossTabCellConditionTest
    {
        static DesignData Design()
        {
            var d = new DesignData();
            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            var status = new SelectFieldDesign { Name = "Status", DbColumn = "status" };
            status.Candidates.AddRange(["進行中,10", "完了,20"]);
            order.Fields.Add(status);
            order.Fields.Add(new DateFieldDesign { Name = "OrderedOn", DbColumn = "ordered_on" });
            order.Fields.Add(new TextFieldDesign { Name = "Owner", DbColumn = "owner" });
            d.AddModule(order);

            var summary = new ModuleDesign { Name = "Summary" };
            var tab = new CrossTabFieldDesign { Name = "Tab" };
            tab.SearchCondition.ModuleName = "Order";
            tab.SearchCondition.Condition = new FieldValueMatchCondition { SearchTargetVariable = "Owner.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("A") };
            tab.Setting.Rows.Add(new ValueGroup { Variable = "Status.Value" });
            tab.Setting.Columns.Add(new DateGroup { Variable = "OrderedOn.Value", Bucket = DateBucket.Month });
            tab.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            tab.Setting.LimitCount = 1;
            summary.Fields.Add(tab);
            d.AddModule(summary);
            return d;
        }

        static AggregateRow Row(decimal count, params object?[] keys)
            => new() { Keys = keys.Select(k => new AggregateKey { Value = MultiTypeValue.Create(k) }).ToList(), Values = [MultiTypeValue.Create(count)] };

        //集計の結果: 行を選ぶ定義 (状態 1 軸) は 進行中 だけ (上限 1)。本体は 進行中 × (1 月 / 空値)
        static List<AggregateResult> Results(List<AggregateCondition> conditions) => conditions.Select(c => c.Groups.Count switch
        {
            0 => new AggregateResult { Rows = [Row(2)], TotalCount = 3, GroupCount = 1 },
            2 => new AggregateResult { Rows = [Row(1, "10", new DateOnly(2026, 1, 1)), Row(1, "10", null)], TotalCount = 2, GroupCount = 2 },
            _ when c.Groups[0] is DateGroup => new AggregateResult { Rows = [Row(1, new DateOnly(2026, 1, 1)), Row(1, (object?)null)], TotalCount = 2, GroupCount = 2 },
            _ => new AggregateResult { Rows = [Row(2, "10")], TotalCount = 3, GroupCount = 2, IsLimited = true },
        }).ToList();

        static async Task<CrossTabField> LoadAsync()
        {
            var svc = new TestServices(Design());
            svc.App.AggregateProvider = Results;
            var module = await svc.CreateModuleAsync("Summary");
            var field = module.GetField<CrossTabField>("Tab")!;
            await field.ReloadAsync();
            Assert.That(field.Table, Is.Not.Null, field.LoadError);
            return field;
        }

        //条件を読める文字にする (AND は "&"、OR は "|"、空値は null)
        static string Text(MatchConditionBase? c) => c switch
        {
            null => "",
            MultiMatchCondition m => "(" + string.Join(m.IsOrMatch ? " | " : " & ", m.Children.Select(Text)) + ")",
            FieldValueMatchCondition v => $"{v.SearchTargetVariable} {v.Comparison} {Value(v.Value.GetValue())}",
            _ => c.GetType().Name,
        };

        static string Value(object? v) => v switch { null => "null", DateOnly d => d.ToString("yyyy-MM-dd"), _ => v.ToString()! };

        const string Owner = "Owner.Value Equal A";
        const string January = "(OrderedOn.Value GreaterThanOrEqual 2026-01-01 & OrderedOn.Value LessThan 2026-02-01)";

        [Test]
        public async Task セルは表の条件と行と列の鍵で日付は期間で空値は空値の行()
        {
            var field = await LoadAsync();
            Assert.That(Text(field.CreateCellCondition(0, 0)), Is.EqualTo($"({Owner} & Status.Value Equal 10 & {January})"));
            Assert.That(Text(field.CreateCellCondition(0, 1)), Is.EqualTo($"({Owner} & Status.Value Equal 10 & OrderedOn.Value Equal null)"));
            //行計はその行 (表に出ている行なので絞らない)
            Assert.That(Text(field.CreateCellCondition(0, null)), Is.EqualTo($"({Owner} & Status.Value Equal 10)"));
        }

        [Test]
        public async Task 上限で行を選んだ表の列計と総計は表に出ている行の分()
        {
            var field = await LoadAsync();
            Assert.That(Text(field.CreateCellCondition(null, 0)), Is.EqualTo($"({Owner} & {January} & (Status.Value Equal 10))"));
            Assert.That(Text(field.CreateCellCondition(null, null)), Is.EqualTo($"({Owner} & (Status.Value Equal 10))"));
        }

        [Test]
        public async Task 利用者が行と列を変えたらそのときの項目で条件を作る()
        {
            var field = await LoadAsync();
            //行と列を入れ替えた設定を当てる (集計の結果は同じ形で返る: 1 軸目が日付・2 軸目が状態)
            var swapped = field.Design.Setting.JsonClone();
            (swapped.Rows, swapped.Columns) = (swapped.Columns, swapped.Rows);
            var svc = new TestServices(Design());
            svc.App.AggregateProvider = conditions => conditions.Select(c => c.Groups.Count switch
            {
                0 => new AggregateResult { Rows = [Row(2)], TotalCount = 2, GroupCount = 1 },
                2 => new AggregateResult { Rows = [Row(1, new DateOnly(2026, 1, 1), "10")], TotalCount = 1, GroupCount = 1 },
                _ when c.Groups[0] is DateGroup => new AggregateResult { Rows = [Row(1, new DateOnly(2026, 1, 1))], TotalCount = 1, GroupCount = 1 },
                _ => new AggregateResult { Rows = [Row(1, "10")], TotalCount = 1, GroupCount = 1 },
            }).ToList();
            var module = await svc.CreateModuleAsync("Summary");
            field = module.GetField<CrossTabField>("Tab")!;
            await field.ApplyUserSettingAsync(swapped, null);

            Assert.That(Text(field.CreateCellCondition(0, 0)), Is.EqualTo($"({Owner} & {January} & Status.Value Equal 10)"));
        }

        [Test]
        public async Task CreateSearcherは元モジュールの検索にセルの条件を入れる()
        {
            var field = await LoadAsync();
            var cell = new CrossTabCell { ModuleName = field.ModuleName, Condition = field.CreateCellCondition(0, 0) };
            var searcher = cell.CreateSearcher();
            var condition = searcher.GetSearchCondition();
            Assert.That(condition.ModuleName, Is.EqualTo("Order"));
            Assert.That(Text(condition.Condition), Is.EqualTo($"(({Owner} & Status.Value Equal 10 & {January}))"));
            //渡した条件を後から変えても検索は変わらない (複製を入れる)
            ((MultiMatchCondition)cell.Condition!).Children.Clear();
            Assert.That(Text(searcher.GetSearchCondition().Condition), Is.EqualTo($"(({Owner} & Status.Value Equal 10 & {January}))"));
        }
    }
}
