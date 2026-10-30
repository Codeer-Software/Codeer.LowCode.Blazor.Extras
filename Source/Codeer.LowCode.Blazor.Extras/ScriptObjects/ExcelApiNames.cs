namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>Excel スクリプトオブジェクトが共通の API (ExtrasTransport / Extras.Server の ExcelApi) で使う機能名と操作名。</summary>
    public static class ExcelApiNames
    {
        public const string Feature = "Excel";

        /// <summary>Excel を PDF に変換する。要求 ExcelConvertPdfRequest (xlsx の中身)、応答 ExcelConvertPdfResponse (PDF の中身)。</summary>
        public const string ConvertPdf = "ConvertPdf";
    }
}
