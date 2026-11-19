using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    //ガントのタスクの開始と終了の前後関係
    internal static class GanttDateRange
    {
        //日時は 開始 < 終了、日付だけのときは 開始 <= 終了 (同じ日 = 1 日のタスク)。
        //どちらかが空なら見ない (必須かどうかは各項目の設定で決まる)。
        //日時は分までで比べる (画面で入力する単位は分。秒より下の端数で、同じに見える日時の判定が割れないように)
        internal static bool IsValid(DateTime? start, DateTime? end, bool isDateOnly)
        {
            if (start == null || end == null) return true;
            return isDateOnly
                ? start.Value.Date <= end.Value.Date
                : TruncateToMinute(start.Value) < TruncateToMinute(end.Value);
        }

        //追加するタスクの初期値。見えている期間 (rangeStart 以上 rangeEnd 未満) に今日があれば今日、無ければ期間の最初の日の 1 日ぶん。
        //日時は その日の 0:00 から翌日の 0:00、日付だけは 開始 = 終了 = その日
        internal static (DateTime Start, DateTime End) GetDefault(DateTime today, DateTime rangeStart, DateTime rangeEnd, bool isDateOnly)
        {
            var day = rangeStart.Date <= today.Date && today.Date < rangeEnd ? today.Date : rangeStart.Date;
            return (day, isDateOnly ? day : day.AddDays(1));
        }

        static DateTime TruncateToMinute(DateTime value)
            => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMinute, value.Kind);

        //前後が逆のときのメッセージ。正しければ null。項目は画面に出ている名前で示す
        internal static string? GetError(DateTime? start, DateTime? end, bool isDateOnly, string startName, string endName)
        {
            if (IsValid(start, end, isDateOnly)) return null;
            var format = isDateOnly ? Resources.GanttEndBeforeStartDate : Resources.GanttEndNotAfterStart;
            return string.Format(format, startName, endName);
        }
    }
}
