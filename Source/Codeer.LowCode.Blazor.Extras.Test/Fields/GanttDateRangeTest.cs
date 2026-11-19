using System.Globalization;
using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// ガントのタスクの開始と終了: 終了が開始より前のタスク (バーが描けない行) を作らせない。
    /// </summary>
    public class GanttDateRangeTest
    {
        static readonly DateTime Start = new(2026, 11, 18, 10, 0, 0);

        [Test]
        public void 日時は終了が開始より後のときだけ通す()
        {
            Assert.That(GanttDateRange.IsValid(Start, Start.AddMinutes(1), false), Is.True);
            Assert.That(GanttDateRange.IsValid(Start, Start, false), Is.False);
            Assert.That(GanttDateRange.IsValid(Start, Start.AddDays(-1), false), Is.False);
        }

        [Test]
        public void 分より下の端数だけが違う日時は同じ日時として扱う()
        {
            Assert.That(GanttDateRange.IsValid(Start, Start.AddMilliseconds(123), false), Is.False);
            Assert.That(GanttDateRange.IsValid(Start, Start.AddSeconds(35), false), Is.False);
            Assert.That(GanttDateRange.IsValid(Start.AddSeconds(35), Start, false), Is.False);
            Assert.That(GanttDateRange.IsValid(Start.AddSeconds(59), Start.AddMinutes(1), false), Is.True);
        }

        [Test]
        public void 追加時の初期値は見えている期間に今日があれば今日の1日ぶん()
        {
            var now = new DateTime(2026, 11, 18, 10, 23, 45, 678);
            var range = (Start: new DateTime(2026, 11, 15), End: new DateTime(2026, 11, 22));

            Assert.That(GanttDateRange.GetDefault(now, range.Start, range.End, false),
                Is.EqualTo((new DateTime(2026, 11, 18), new DateTime(2026, 11, 19))));
            //期間の最初の日と最後の日も「今日がある」に入る
            Assert.That(GanttDateRange.GetDefault(new DateTime(2026, 11, 15, 0, 0, 0), range.Start, range.End, false).Start,
                Is.EqualTo(new DateTime(2026, 11, 15)));
            Assert.That(GanttDateRange.GetDefault(new DateTime(2026, 11, 21, 23, 59, 0), range.Start, range.End, false).Start,
                Is.EqualTo(new DateTime(2026, 11, 21)));
        }

        [Test]
        public void 追加時の初期値は見えている期間に今日が無ければ期間の最初の日()
        {
            var range = (Start: new DateTime(2026, 11, 22), End: new DateTime(2026, 11, 29));

            //今日が期間より前 / 期間の終わり (含まない) / 期間より後
            foreach (var now in new[] { new DateTime(2026, 11, 18, 10, 23, 0), new DateTime(2026, 11, 29), new DateTime(2026, 12, 5) })
            {
                Assert.That(GanttDateRange.GetDefault(now, range.Start, range.End, false),
                    Is.EqualTo((new DateTime(2026, 11, 22), new DateTime(2026, 11, 23))));
            }
        }

        [Test]
        public void 追加時の初期値は日付だけのとき開始と終了が同じ日()
        {
            var now = new DateTime(2026, 11, 18, 10, 23, 0);

            Assert.That(GanttDateRange.GetDefault(now, new DateTime(2026, 11, 15), new DateTime(2026, 11, 22), true),
                Is.EqualTo((new DateTime(2026, 11, 18), new DateTime(2026, 11, 18))));
            Assert.That(GanttDateRange.GetDefault(now, new DateTime(2026, 12, 1), new DateTime(2027, 1, 1), true),
                Is.EqualTo((new DateTime(2026, 12, 1), new DateTime(2026, 12, 1))));
        }

        [Test]
        public void 追加時の初期値は開始と終了の検証を通る()
        {
            var now = new DateTime(2026, 11, 18, 10, 23, 0);
            foreach (var isDateOnly in new[] { false, true })
            {
                var (start, end) = GanttDateRange.GetDefault(now, new DateTime(2026, 11, 15), new DateTime(2026, 11, 22), isDateOnly);
                Assert.That(GanttDateRange.IsValid(start, end, isDateOnly), Is.True);
            }
        }

        [Test]
        public void 日付だけのときは同じ日も通す()
        {
            Assert.That(GanttDateRange.IsValid(Start.Date, Start.Date.AddDays(1), true), Is.True);
            Assert.That(GanttDateRange.IsValid(Start.Date, Start.Date, true), Is.True);
            Assert.That(GanttDateRange.IsValid(Start.Date, Start.Date.AddDays(-1), true), Is.False);
        }

        [Test]
        public void どちらかが空なら見ない()
        {
            Assert.That(GanttDateRange.IsValid(null, Start, false), Is.True);
            Assert.That(GanttDateRange.IsValid(Start, null, false), Is.True);
            Assert.That(GanttDateRange.IsValid(null, null, true), Is.True);
        }

        [Test]
        public void エラーは項目の名前で直し方を示す()
        {
            Assert.That(Error("ja-JP", Start, Start.AddDays(-1), false), Is.EqualTo("終了は開始より後にしてください。"));
            Assert.That(Error("ja-JP", Start, Start.AddDays(-1), true), Is.EqualTo("終了は開始と同じ日か、それより後にしてください。"));
            Assert.That(Error("en-US", Start, Start.AddDays(-1), false), Is.EqualTo("終了 must be later than 開始."));
            Assert.That(Error("ja-JP", Start, Start.AddDays(1), false), Is.Null);
        }

        //文言はリソース (表示言語) で決まるので、表示言語を切り替えて組み立てる
        static string? Error(string cultureName, DateTime start, DateTime end, bool isDateOnly)
        {
            var backup = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                return GanttDateRange.GetError(start, end, isDateOnly, "開始", "終了");
            }
            finally
            {
                CultureInfo.CurrentUICulture = backup;
            }
        }
    }
}
