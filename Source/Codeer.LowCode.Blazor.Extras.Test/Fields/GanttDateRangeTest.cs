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
        public void 追加時の初期値は分までにする()
        {
            Assert.That(GanttDateRange.TruncateToMinute(new DateTime(2026, 11, 18, 10, 23, 45, 678)), Is.EqualTo(new DateTime(2026, 11, 18, 10, 23, 0)));
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
