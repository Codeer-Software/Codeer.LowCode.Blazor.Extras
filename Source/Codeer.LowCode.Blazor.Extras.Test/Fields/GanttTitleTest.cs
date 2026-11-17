using System.Globalization;
using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// ガントのタイトル: どの表示でも年を必ず出す (年が空のまま「年11月 15日 - 21日」にならない)。
    /// </summary>
    public class GanttTitleTest
    {
        //書式はリソース (表示言語) で決まるので、表示言語を切り替えて組み立てる
        static string In(string cultureName, Func<CultureInfo, string> title)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var backup = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = culture;
                return title(culture);
            }
            finally
            {
                CultureInfo.CurrentUICulture = backup;
            }
        }

        static string Week(string cultureName, DateTime start, DateTime lastDay)
            => In(cultureName, c => GanttTitle.Week(start, lastDay, c));

        static string Month(string cultureName, DateTime start, DateTime lastDay)
            => In(cultureName, c => GanttTitle.Month(start, lastDay, c));

        [Test]
        public void 日表示は年月日を出す()
        {
            Assert.That(In("ja-JP", c => GanttTitle.Day(new DateTime(2026, 11, 15), c)), Is.EqualTo("2026年11月15日"));
            Assert.That(In("en-US", c => GanttTitle.Day(new DateTime(2026, 11, 15), c)), Is.EqualTo("November 15, 2026"));
        }

        [Test]
        public void 月表示は年と月を出す()
        {
            Assert.That(Month("ja-JP", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)), Is.EqualTo("2026年11月"));
            Assert.That(Month("en-US", new DateTime(2026, 11, 1), new DateTime(2026, 11, 30)), Is.EqualTo("November 2026"));
        }

        [Test]
        public void 月をまたぐ月表示は両方の年と月を出す()
        {
            Assert.That(Month("ja-JP", new DateTime(2026, 11, 1), new DateTime(2026, 12, 12)), Is.EqualTo("2026年11月 - 2026年12月"));
            Assert.That(Month("en-US", new DateTime(2026, 11, 1), new DateTime(2026, 12, 12)), Is.EqualTo("Nov 2026 - Dec 2026"));
            Assert.That(Month("ja-JP", new DateTime(2026, 12, 1), new DateTime(2027, 1, 11)), Is.EqualTo("2026年12月 - 2027年1月"));
        }

        [Test]
        public void 同じ月の週は年と月を1回ずつ出す()
        {
            Assert.That(Week("ja-JP", new DateTime(2026, 11, 15), new DateTime(2026, 11, 21)), Is.EqualTo("2026年11月 15日 - 21日"));
            Assert.That(Week("en-US", new DateTime(2026, 11, 15), new DateTime(2026, 11, 21)), Is.EqualTo("November 15 - 21, 2026"));
        }

        [Test]
        public void 月をまたぐ週は年を1回と両方の月を出す()
        {
            Assert.That(Week("ja-JP", new DateTime(2026, 11, 29), new DateTime(2026, 12, 5)), Is.EqualTo("2026年11月29日 - 12月5日"));
            Assert.That(Week("en-US", new DateTime(2026, 11, 29), new DateTime(2026, 12, 5)), Is.EqualTo("Nov 29 - Dec 5, 2026"));
        }

        [Test]
        public void 年をまたぐ週は両方の年を出す()
        {
            Assert.That(Week("ja-JP", new DateTime(2026, 12, 27), new DateTime(2027, 1, 2)), Is.EqualTo("2026年12月27日 - 2027年1月2日"));
            Assert.That(Week("en-US", new DateTime(2026, 12, 27), new DateTime(2027, 1, 2)), Is.EqualTo("Dec 27, 2026 - Jan 2, 2027"));
        }
    }
}
