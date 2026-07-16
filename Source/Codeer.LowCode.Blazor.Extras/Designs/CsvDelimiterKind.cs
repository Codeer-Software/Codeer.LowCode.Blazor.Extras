namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>CSV の区切り文字。</summary>
    public enum CsvDelimiterKind
    {
        Comma,
        Tab,
        Semicolon,
    }

    /// <summary>CsvDelimiterKind の拡張。</summary>
    public static class CsvDelimiterKindExtensions
    {
        /// <summary>区切り文字の実効値。</summary>
        public static char ToChar(this CsvDelimiterKind kind) => kind switch
        {
            CsvDelimiterKind.Tab => '\t',
            CsvDelimiterKind.Semicolon => ';',
            _ => ',',
        };
    }
}
