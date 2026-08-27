using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>ExcelReportButtonField の出力形式。</summary>
    public enum ExcelReportFormat
    {
        [Designer(DisplayName = "$ExcelReportFormat_Xlsx")] Xlsx,
        [Designer(DisplayName = "$ExcelReportFormat_Pdf")] Pdf,
    }
}
