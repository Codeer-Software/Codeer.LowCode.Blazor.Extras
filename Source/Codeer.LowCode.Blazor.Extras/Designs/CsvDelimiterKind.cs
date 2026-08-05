namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 区切り文字。None = 区切り文字なし = 固定長形式
    /// (列は桁位置で決まる。列幅は FileColumnMappingField の列ごとに設定、併用必須)。
    /// </summary>
    public enum CsvDelimiterKind
    {
        Comma,
        Tab,
        Semicolon,
        None,
    }

    /// <summary>CsvDelimiterKind の拡張。</summary>
    public static class CsvDelimiterKindExtensions
    {
        /// <summary>区切り文字の実効値 (None は固定長経路で使われないため呼ばれない想定。防御的にカンマ)。</summary>
        public static char ToChar(this CsvDelimiterKind kind) => kind switch
        {
            CsvDelimiterKind.Tab => '\t',
            CsvDelimiterKind.Semicolon => ';',
            _ => ',',
        };
    }
}
