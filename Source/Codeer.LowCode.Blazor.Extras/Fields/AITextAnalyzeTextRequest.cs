namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>文章の AI 解析の要求 (共通の API の AITextAnalyzeApiNames.AnalyzeText)。</summary>
    public class AITextAnalyzeTextRequest
    {
        /// <summary>AITextAnalyzerField を持つモジュール。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>AITextAnalyzerField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>解析する文章。</summary>
        public string Text { get; set; } = string.Empty;
    }
}
