namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>進捗値のスケール解釈。ProgressField / GanttField で共有する。</summary>
    public enum ProgressScale
    {
        /// <summary>値をそのままパーセントとして扱う (100 で 100%)。</summary>
        Percent,

        /// <summary>0.0〜1.0 の割合として扱う (1.0 で 100%)。</summary>
        Ratio,
    }

    internal static class ProgressScaleExtensions
    {
        /// <summary>スケールを適用して 0〜100(%) の充填率へ変換する。null は 0%、範囲外はクランプ。</summary>
        public static double ToFillPercent(this ProgressScale scale, decimal? value)
        {
            if (value == null) return 0d;
            var v = (double)value.Value;
            if (scale == ProgressScale.Ratio) v *= 100d;
            return Math.Clamp(v, 0d, 100d);
        }
    }
}
