using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.CrossTab
{
    // クロス集計フィールドのデザイン: 集計定義への変換・JSON の往復・デザインチェック (値なし / 使えない計算 / 日付でない項目の丸め / 番号の範囲 / 無い項目)
    public class CrossTabFieldDesignTest
    {
        static DesignData Design(Action<CrossTabFieldDesign>? configure = null)
        {
            var d = new DesignData();
            var customer = new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Region", DbColumn = "region" });
            d.AddModule(customer);
            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            order.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "amount" });
            order.Fields.Add(new DateFieldDesign { Name = "OrderedOn", DbColumn = "ordered_on" });
            order.Fields.Add(new LinkFieldDesign { Name = "Customer", DbColumn = "customer_id", SearchCondition = new SearchCondition("Customer"), ValueVariable = "Id.Value", DisplayTextVariable = "Region.Value" });
            d.AddModule(order);
            var dashboard = new ModuleDesign { Name = "Dashboard" };
            var field = new CrossTabFieldDesign { Name = "Summary", SearchCondition = new SearchCondition("Order") };
            field.Setting.Rows.Add(new ValueGroup { Variable = "Customer.Region.Value" });
            field.Setting.Columns.Add(new DateGroup { Variable = "OrderedOn.Value", Bucket = DateBucket.Month });
            field.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value", Name = "金額" });
            field.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            configure?.Invoke(field);
            dashboard.Fields.Add(field);
            d.AddModule(dashboard);
            return d;
        }

        static List<DesignCheckInfo> Check(DesignData d)
        {
            var module = d.Modules.Find("Dashboard")!;
            var context = new DesignCheckContext(module.Name, d, new());
            return module.Fields.OfType<CrossTabFieldDesign>().Single().CheckDesign(context);
        }

        [Test]
        public void 集計定義への変換は行と列の順に軸を並べ設定を写す()
        {
            var field = Design().Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();
            field.Setting.Having.Add(new AggregateHaving { MeasureIndex = 0, Comparison = MatchComparison.GreaterThan, Value = 100 });
            field.Setting.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Measure, Index = 0, IsDescending = true });
            field.Setting.LimitCount = 20;

            var c = field.CreateAggregateCondition();

            Assert.That(c.ModuleName, Is.EqualTo("Order"));
            Assert.That(c.Groups.Select(g => g.Variable), Is.EqualTo(new[] { "Customer.Region.Value", "OrderedOn.Value" }));
            Assert.That(((DateGroup)c.Groups[1]).Bucket, Is.EqualTo(DateBucket.Month));
            Assert.That(c.Measures.Count, Is.EqualTo(2));
            Assert.That(c.Having.Single().Value, Is.EqualTo(100m));
            Assert.That(c.SortConditions.Single().IsDescending, Is.True);
            Assert.That(c.LimitCount, Is.EqualTo(20));
            //実行時の軸を差し替えられる
            var runtime = field.CreateAggregateCondition([new ValueGroup { Variable = "Title.Value" }], []);
            Assert.That(runtime.Groups.Select(g => g.Variable), Is.EqualTo(new[] { "Title.Value" }));
        }

        [Test]
        public void JSONを往復しても設定が保たれる()
        {
            var field = Design().Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();
            field.ValueDisplay = CrossTabValueDisplay.PercentOfRow;

            var json = JsonConverterEx.SerializeObject(field);
            var restored = JsonConverterEx.DeserializeObject<FieldDesignBase>(json) as CrossTabFieldDesign;

            Assert.That(restored, Is.Not.Null);
            Assert.That(((DateGroup)restored!.Setting.Columns.Single()).Bucket, Is.EqualTo(DateBucket.Month));
            Assert.That(restored.Setting.Measures[0].Name, Is.EqualTo("金額"));
            Assert.That(restored.ValueDisplay, Is.EqualTo(CrossTabValueDisplay.PercentOfRow));
            Assert.That(restored.Setting.GetCurrentSettings(), Is.EqualTo("Customer.Region x OrderedOn(Month) : Sum(Amount), Count"));
        }

        [Test]
        public void 正しい設計は指摘なし()
        {
            Assert.That(Check(Design()).Count, Is.EqualTo(0));
        }

        [Test]
        public void 値なし_使えない計算_日付でない丸め_範囲外の番号_無い項目は指摘される()
        {
            var noMeasure = Check(Design(f => f.Setting.Measures.Clear()));
            Assert.That(noMeasure.Any(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.NoMeasure)), Is.True);

            var sumOfText = Check(Design(f => f.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Title.Value" })));
            Assert.That(sumOfText.Any(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.InvalidFunction)), Is.True);

            var monthOfText = Check(Design(f => f.Setting.Rows.Add(new DateGroup { Variable = "Title.Value", Bucket = DateBucket.Month })));
            Assert.That(monthOfText.Any(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.DateBucketRequiresDate)), Is.True);

            var badIndex = Check(Design(f =>
            {
                f.Setting.Having.Add(new AggregateHaving { MeasureIndex = 5 });
                f.Setting.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Group, Index = 2 });
            }));
            Assert.That(badIndex.Count(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.IndexOutOfRange)), Is.EqualTo(2));

            var unknown = Check(Design(f => f.Setting.Rows.Add(new ValueGroup { Variable = "Nothing.Value" })));
            Assert.That(unknown.Count, Is.EqualTo(1));
        }

        [Test]
        public void リンク越しの項目もリンク先の型で指摘される()
        {
            //Customer.Region はリンク先 (Customer) の文字項目: 月の丸めも合計もできない
            var monthOfLinkedText = Check(Design(f => f.Setting.Columns.Add(new DateGroup { Variable = "Customer.Region.Value", Bucket = DateBucket.Month })));
            Assert.That(monthOfLinkedText.Any(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.DateBucketRequiresDate)), Is.True);
            var sumOfLinkedText = Check(Design(f => f.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Customer.Region.Value" })));
            Assert.That(sumOfLinkedText.Any(e => e.Code == DesignCheckCode.Create(typeof(CrossTabFieldDesign), CrossTabFieldDesign.Codes.InvalidFunction)), Is.True);

            var d = Design();
            Assert.That(CrossTabFieldDesign.ResolveField(d, d.Modules.Find("Order")!, "Customer.Region.Value"), Is.InstanceOf<TextFieldDesign>());
            Assert.That(CrossTabFieldDesign.ResolveField(d, d.Modules.Find("Order")!, "Customer.Nothing.Value"), Is.Null);
        }

        [Test]
        public void モジュール参照の選択項目もリンクとしてたどれて候補に出て利用者の設定から捨てられない()
        {
            var d = Design();
            d.Modules.Find("Order")!.Fields.Add(new SelectFieldDesign { Name = "Buyer", DbColumn = "buyer_id", SearchCondition = new SearchCondition("Customer"), ValueVariable = "Id.Value", DisplayTextVariable = "Region.Value" });
            var field = d.Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();

            Assert.That(CrossTabFieldDesign.ResolveField(d, d.Modules.Find("Order")!, "Buyer.Region.Value"), Is.InstanceOf<TextFieldDesign>());
            Assert.That(field.GetFieldCandidates(d, s => s).Select(e => e.Variable), Does.Contain("Buyer.Region.Value"));
            var saved = new CrossTabSetting();
            saved.Rows.Add(new ValueGroup { Variable = "Buyer.Region.Value" });
            saved.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            Assert.That(field.ValidateSetting(saved, d, includeUnknownField: true), Is.Empty);
            Assert.That(field.ReconcileSetting(saved, d)!.Rows.Select(e => e.Variable), Is.EqualTo(new[] { "Buyer.Region.Value" }));
            //候補値の選択 (モジュール参照でない) はリンクではない
            Assert.That(CrossTabFieldDesign.LinkTargetModuleName(new SelectFieldDesign { Name = "Status" }), Is.Null);
        }

        [Test]
        public void 利用者の設定の検査は無い項目も指摘する()
        {
            var d = Design();
            var field = d.Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();
            Assert.That(field.ValidateSetting(field.Setting, d, includeUnknownField: true), Is.Empty);

            var setting = field.Setting.JsonClone();
            setting.Rows.Add(new ValueGroup { Variable = "Nothing.Value" });
            setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Title.Value" });
            var codes = field.ValidateSetting(setting, d, includeUnknownField: true).Select(e => e.Code).ToList();
            Assert.That(codes, Does.Contain(CrossTabFieldDesign.Codes.UnknownField));
            Assert.That(codes, Does.Contain(CrossTabFieldDesign.Codes.InvalidFunction));
            //デザインチェック向け (includeUnknownField: false) は無い項目を返さない
            Assert.That(field.ValidateSetting(setting, d, includeUnknownField: false).Select(e => e.Code), Does.Not.Contain(CrossTabFieldDesign.Codes.UnknownField));
        }

        [Test]
        public void 保存した設定は今の設計に合わせて無い項目を捨て番号を詰める()
        {
            var d = Design();
            var field = d.Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();
            var saved = new CrossTabSetting { LimitCount = 10 };
            saved.Rows.Add(new ValueGroup { Variable = "Removed.Value" });                                   //行 0: 消えた項目
            saved.Rows.Add(new ValueGroup { Variable = "Customer.Region.Value" });                           //行 1 → 0
            saved.Columns.Add(new DateGroup { Variable = "OrderedOn.Value", Bucket = DateBucket.Year }); //列 (通し 2) → 1
            saved.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Gone.Value" });   //値 0: 消えた項目
            saved.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value" }); //値 1 → 0
            saved.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count, Name = "件数" });          //値 2 → 1
            saved.Having.Add(new AggregateHaving { MeasureIndex = 0, Comparison = MatchComparison.GreaterThan, Value = 1 }); //消えた値を指す
            saved.Having.Add(new AggregateHaving { MeasureIndex = 2, Comparison = MatchComparison.GreaterThan, Value = 5 });
            saved.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Group, Index = 0 });                  //消えた行を指す
            saved.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Group, Index = 2, IsDescending = true });
            saved.SortConditions.Add(new AggregateSort { Target = AggregateSortTarget.Measure, Index = 1 });

            var r = field.ReconcileSetting(saved, d)!;

            Assert.That(r.Rows.Select(g => g.Variable), Is.EqualTo(new[] { "Customer.Region.Value" }));
            Assert.That(((DateGroup)r.Columns.Single()).Bucket, Is.EqualTo(DateBucket.Year));
            Assert.That(r.Measures.Select(m => m.Function), Is.EqualTo(new[] { AggregateFunction.Sum, AggregateFunction.Count }));
            Assert.That(r.Having.Single().MeasureIndex, Is.EqualTo(1));
            Assert.That(r.SortConditions.Select(s => (s.Target, s.Index)), Is.EqualTo(new[] { (AggregateSortTarget.Group, 1), (AggregateSortTarget.Measure, 0) }));
            Assert.That(r.LimitCount, Is.EqualTo(10));
            Assert.That(field.ValidateSetting(r, d, includeUnknownField: true), Is.Empty);
        }

        [Test]
        public void 保存した設定の値が全部消えていたら設計の設定に戻る()
        {
            var d = Design();
            var field = d.Modules.Find("Dashboard")!.Fields.OfType<CrossTabFieldDesign>().Single();
            var saved = new CrossTabSetting();
            saved.Rows.Add(new ValueGroup { Variable = "Customer.Region.Value" });
            saved.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Gone.Value" });
            Assert.That(field.ReconcileSetting(saved, d), Is.Null);
        }

        [Test]
        public void 元モジュールが空の設計はスクリプト用で指摘なし()
        {
            var d = Design(f => { f.SearchCondition = new SearchCondition(); f.Setting = new CrossTabSetting(); });
            Assert.That(Check(d).Count, Is.EqualTo(0));
        }
    }
}
