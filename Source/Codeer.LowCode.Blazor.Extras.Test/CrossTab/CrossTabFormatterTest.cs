using System.Globalization;
using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.CrossTab
{
    // セルと見出しの文字: 値の Format → 元の項目の Format → 既定。件数は整数。真偽は TrueText / FalseText、数値の軸は項目の Format
    public class CrossTabFormatterTest
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static MultiTypeValue V(decimal d) => MultiTypeValue.Create(d);

        [Test]
        public void 件数は書式に関わらず整数()
        {
            var count = new AggregateMeasure { Function = AggregateFunction.Count, Format = "P0" };
            Assert.That(CrossTabFormatter.Measure(V(1234m), count, null, Inv), Is.EqualTo("1,234"));
        }

        [Test]
        public void 値の書式が無ければ元の項目の書式で出し確度の平均は40パーセントになる()
        {
            var probability = new NumberFieldDesign { Name = "Probability", Format = "P0" };
            var avg = new AggregateMeasure { Function = AggregateFunction.Avg, Variable = "Probability.Value" };
            Assert.That(CrossTabFormatter.Measure(V(0.4m), avg, probability, Inv), Is.EqualTo("40 %"));
        }

        [Test]
        public void 値の書式は元の項目の書式より優先し不正な書式は既定で出す()
        {
            var amount = new NumberFieldDesign { Name = "Amount", Format = "C0" };
            var avg = new AggregateMeasure { Function = AggregateFunction.Avg, Variable = "Amount.Value", Format = "N1" };
            Assert.That(CrossTabFormatter.Measure(V(1234.56m), avg, amount, Inv), Is.EqualTo("1,234.6"));
            var broken = new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value", Format = "X" };   // decimal に使えない書式 (16 進) は既定で出す
            Assert.That(CrossTabFormatter.Measure(V(1234.5m), broken, null, Inv), Is.EqualTo("1,234.5"));
        }

        [Test]
        public void 書式が無ければ桁区切りで小数は2桁まで()
        {
            var avg = new AggregateMeasure { Function = AggregateFunction.Avg, Variable = "Amount.Value" };
            Assert.That(CrossTabFormatter.Measure(V(333.3333m), avg, new NumberFieldDesign { Name = "Amount" }, Inv), Is.EqualTo("333.33"));
            Assert.That(CrossTabFormatter.Measure(V(1500000m), avg, null, Inv), Is.EqualTo("1,500,000"));
            Assert.That(CrossTabFormatter.Measure(new NullValue(), avg, null, Inv), Is.EqualTo(""));
        }

        [Test]
        public void 軸の真偽は項目の文言で数値は項目の書式で出る()
        {
            var flag = new BooleanFieldDesign { Name = "IsActive", TrueText = "有効", FalseText = "無効" };
            var group = new ValueGroup { Variable = "IsActive.Value" };
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = MultiTypeValue.Create(true) }, group, flag, "(空白)", "はい", "いいえ", Inv), Is.EqualTo("有効"));
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = MultiTypeValue.Create(false) }, group, new BooleanFieldDesign { Name = "X" }, "(空白)", "はい", "いいえ", Inv), Is.EqualTo("いいえ"));

            var rate = new NumberFieldDesign { Name = "Rate", Format = "P0" };
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = V(0.25m) }, new ValueGroup { Variable = "Rate.Value" }, rate, "(空白)", "", "", Inv), Is.EqualTo("25 %"));
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = new NullValue() }, group, flag, "(空白)", "", "", Inv), Is.EqualTo("(空白)"));
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = V(3m), DisplayText = "高" }, group, null, "(空白)", "", "", Inv), Is.EqualTo("高"));
        }

        [Test]
        public void UTCで保存した日時と時刻はローカル時刻で出し時でまとめた軸はそのまま()
        {
            var utc = new DateTime(2026, 4, 1, 15, 30, 0);
            var utcTime = new TimeOnly(15, 30);
            var localText = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv);
            var localTimeText = TimeOnly.FromDateTime(new DateTime(new DateOnly(2000, 1, 1), utcTime, DateTimeKind.Utc).ToLocalTime()).ToString("HH:mm", Inv);

            var utcDateTime = new DateTimeFieldDesign { Name = "VisitedAt", SaveAsUtc = true };
            var localDateTime = new DateTimeFieldDesign { Name = "PlannedAt" };
            var utcTimeField = new TimeFieldDesign { Name = "StartTime", SaveAsUtc = true };
            var max = new AggregateMeasure { Function = AggregateFunction.Max, Variable = "VisitedAt.Value" };

            //値 (最小・最大)
            Assert.That(CrossTabFormatter.Measure(MultiTypeValue.Create(utc), max, utcDateTime, Inv), Is.EqualTo(localText));
            Assert.That(CrossTabFormatter.Measure(MultiTypeValue.Create(utc), max, localDateTime, Inv), Is.EqualTo("2026-04-01 15:30"));
            Assert.That(CrossTabFormatter.Measure(MultiTypeValue.Create(utcTime), max, utcTimeField, Inv), Is.EqualTo(localTimeText));

            //まとめない軸はローカル、時でまとめた軸は SQL で時差を足し済なのでそのまま
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = MultiTypeValue.Create(utc) }, new ValueGroup { Variable = "VisitedAt.Value" }, utcDateTime, "(空白)", "", "", Inv), Is.EqualTo(localText));
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = MultiTypeValue.Create(utcTime) }, new ValueGroup { Variable = "StartTime.Value" }, utcTimeField, "(空白)", "", "", Inv), Is.EqualTo(localTimeText));
            Assert.That(CrossTabFormatter.Key(new AggregateKey { Value = MultiTypeValue.Create(utc) }, new DateGroup { Variable = "VisitedAt.Value", Bucket = DateBucket.Hour }, utcDateTime, "(空白)", "", "", Inv), Is.EqualTo("2026-04-01 15:30"));
        }
    }
}
