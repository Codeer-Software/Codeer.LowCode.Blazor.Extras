namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>Excel → PDF 変換の応答 (共通の API の ExcelApiNames.ConvertPdf)。中身は JSON では base64 になる。</summary>
    public class ExcelConvertPdfResponse
    {
        /// <summary>PDF の中身。</summary>
        public byte[] Content { get; set; } = [];
    }
}
