using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Server.Excel
{
    public static class ExcelPdfService
    {
        public static MemoryStream ConvertToPdf(MemoryStream excelStream)
            => ExcelConverter.ConvertToPdf(excelStream);
    }
}
