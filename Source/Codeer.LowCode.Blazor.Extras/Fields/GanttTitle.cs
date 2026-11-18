using System.Globalization;
using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    //ガントのツールバーに出す表示範囲のタイトル。どの表示でも年を出す
    internal static class GanttTitle
    {
        //日表示のタイトル。日表示のヘッダーは時刻だけなので、曜日もここに出す
        internal static string Day(DateTime date, CultureInfo culture)
            => date.ToString(Resources.DateFormatDayTitleWithWeekday, culture);

        //月表示のタイトル。lastDay は表示範囲の最後の日 (幅に合わせない表示は 42 日ぶんで、月をまたぐ)
        internal static string Month(DateTime start, DateTime lastDay, CultureInfo culture)
        {
            if (start.Year == lastDay.Year && start.Month == lastDay.Month)
                return start.ToString(Resources.DateFormatMonthTitle, culture);
            return string.Format(Resources.RangeTitleFormat,
                start.ToString(Resources.DateFormatMonthYearShort, culture),
                lastDay.ToString(Resources.DateFormatMonthYearShort, culture));
        }

        //週表示のタイトル (CalendarField の週のタイトルと同じ形)。lastDay は表示範囲の最後の日
        internal static string Week(DateTime start, DateTime lastDay, CultureInfo culture)
        {
            if (start.Year != lastDay.Year)
                return string.Format(Resources.RangeTitleFormat,
                    start.ToString(Resources.DateFormatDateShort, culture),
                    lastDay.ToString(Resources.DateFormatDateShort, culture));
            if (start.Month != lastDay.Month)
                return string.Format(Resources.WeekTitleDiffMonth,
                    start.ToString(Resources.DateFormatMonthNameShort, culture), start.Day,
                    lastDay.ToString(Resources.DateFormatMonthNameShort, culture), lastDay.Day, start.Year);
            return string.Format(Resources.WeekTitleSameMonth,
                start.ToString(Resources.DateFormatMonthNameFull, culture), start.Day, lastDay.Day, start.Year);
        }
    }
}
