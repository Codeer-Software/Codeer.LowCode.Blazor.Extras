namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>Excel → PDF 変換の要求 (共通の API の ExcelApiNames.ConvertPdf)。中身は JSON では base64 になる。</summary>
    public class ExcelConvertPdfRequest
    {
        /// <summary>xlsx の中身。</summary>
        public byte[] Content { get; set; } = [];
    }
}
