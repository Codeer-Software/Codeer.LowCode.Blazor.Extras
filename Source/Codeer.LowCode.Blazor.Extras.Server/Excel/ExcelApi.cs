using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Jobs;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Server.Excel
{
    /// <summary>
    /// 共通の API で受ける Excel → PDF 変換 (Excel スクリプトオブジェクトの DownloadPdf)。変換は Excel.Report.PDF。
    /// </summary>
    public sealed class ExcelApi : IExtrasApiHandler
    {
        static readonly object _jobScriptSync = new();
        static bool _jobScriptRegistered;

        /// <summary>
        /// Job スクリプトで Excel (テンプレートへの書き込み・メールの添付・レコードの添付ファイル。PDF は Excel.Report.PDF で変換) を使えるようにする (プロセスの起動時に呼ぶ)。
        /// Excel.ConvertPdf が未設定ならサーバーの変換を入れる (PDF のフォントはホストが設定する)。何度呼んでも登録は 1 回。
        /// </summary>
        public static void UseInJobScripts()
        {
            lock (_jobScriptSync)
            {
                ScriptObjects.Excel.ConvertPdf ??= stream => ExcelConverter.ConvertToPdf(stream);
                if (_jobScriptRegistered) return;
                _jobScriptRegistered = true;
            }
            JobExecutor.AddScriptTypes(JobScriptTypes.AddExcel);
        }

        //テスト用: UseInJobScripts を呼ぶ前に戻す (本体の登録は本体の側で消す)
        internal static void ResetJobScriptsForTest()
        {
            lock (_jobScriptSync) _jobScriptRegistered = false;
        }

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
