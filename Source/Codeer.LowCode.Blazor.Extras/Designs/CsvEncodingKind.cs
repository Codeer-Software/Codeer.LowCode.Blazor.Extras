using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>CSV のエンコーディング。</summary>
    public enum CsvEncodingKind
    {
        [Designer(DisplayName = "$CsvEncodingKind_Utf8Bom")] Utf8Bom,
        [Designer(DisplayName = "$CsvEncodingKind_Utf8")] Utf8,
        [Designer(DisplayName = "$CsvEncodingKind_ShiftJis")] ShiftJis,
    }
}
