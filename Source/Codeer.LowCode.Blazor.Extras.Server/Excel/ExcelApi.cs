using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Server.Excel
{
    /// <summary>
    /// 共通の API で受ける Excel → PDF 変換 (Excel スクリプトオブジェクトの DownloadPdf)。変換は Excel.Report.PDF。
    /// </summary>
    public sealed class ExcelApi : IExtrasApiHandler
    {
        public string Feature => ExcelApiNames.Feature;

        public AuditCategory GetAuditCategory(string action) => action switch
        {
            ExcelApiNames.ConvertPdf => AuditCategory.Export,
            _ => AuditCategory.Other,
        };

        public Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            switch (action)
            {
                case ExcelApiNames.ConvertPdf:
                {
                    using var excel = new MemoryStream(ExtrasApi.ReadPayload<ExcelConvertPdfRequest>(payload).Content);
                    using var pdf = ExcelConverter.ConvertToPdf(excel);
                    return Task.FromResult<object?>(new ExcelConvertPdfResponse { Content = ToBytes(pdf) });
                }
                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        static byte[] ToBytes(Stream stream)
        {
            if (stream is MemoryStream memory) return memory.ToArray();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }
}
