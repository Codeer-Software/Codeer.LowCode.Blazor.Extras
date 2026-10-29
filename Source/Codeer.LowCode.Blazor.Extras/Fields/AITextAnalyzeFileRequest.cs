namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>ファイルの AI 解析の要求 (共通の API の AITextAnalyzeApiNames.AnalyzeFile)。中身は JSON では base64 になる。</summary>
    public class AITextAnalyzeFileRequest
    {
        /// <summary>AITextAnalyzerField を持つモジュール。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>AITextAnalyzerField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>ファイル名 (種類の判定に使う)。</summary>
        public string FileName { get; set; } = string.Empty;

        /// <summary>ファイルの中身。</summary>
        public byte[] Content { get; set; } = [];
    }
}
