using Codeer.LowCode.Blazor.Extras.Properties;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 値の書式の候補 (.NET の数値の書式文字列と、利用者向けの説明)。候補に無い書式も自由に入力できる。空は「元の項目の書式」。
    /// </summary>
    internal static class CrossTabFormatPresets
    {
        public static IReadOnlyList<(string Code, string Text)> All =>
        [
            ("N0", Resources.CrossTab_Format_N0),
            ("N1", Resources.CrossTab_Format_N1),
            ("N2", Resources.CrossTab_Format_N2),
            ("P0", Resources.CrossTab_Format_P0),
            ("P1", Resources.CrossTab_Format_P1),
            ("C0", Resources.CrossTab_Format_C0),
        ];

        /// <summary>候補の説明 (候補に無ければ書式そのもの)。</summary>
        public static string TextOf(string code)
        {
            foreach (var (c, text) in All) if (c == code) return text;
            return code;
        }
    }
}
