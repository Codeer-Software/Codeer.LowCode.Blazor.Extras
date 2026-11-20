using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    //ガントのタスクの進捗として入力できる範囲
    internal static class GanttProgressRange
    {
        //Percent は 0 〜 100、Ratio は 0 〜 1
        internal static (decimal Min, decimal Max) Get(ProgressScale scale)
            => scale == ProgressScale.Ratio ? (0m, 1m) : (0m, 100m);

        //空なら見ない (必須かどうかは項目の設定に任せる)
        internal static bool IsValid(decimal? value, ProgressScale scale)
        {
            if (value == null) return true;
            var (min, max) = Get(scale);
            return min <= value && value <= max;
        }

        //範囲の外のときのメッセージ。正しければ null
        internal static string? GetError(decimal? value, ProgressScale scale, string name)
        {
            if (IsValid(value, scale)) return null;
            var (min, max) = Get(scale);
            return string.Format(Resources.GanttProgressOutOfRange, name, min, max);
        }
    }
}
