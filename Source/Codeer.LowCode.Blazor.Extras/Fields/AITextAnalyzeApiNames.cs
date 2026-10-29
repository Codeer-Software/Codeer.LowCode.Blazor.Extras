namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>AI 文書解析 (AITextAnalyzerField) が共通の API (ExtrasTransport / Extras.Server の AITextAnalyzeApi) で使う機能名と操作名。</summary>
    public static class AITextAnalyzeApiNames
    {
        public const string Feature = "AITextAnalyze";

        /// <summary>ファイルを解析してモジュールのデータにする。要求 AITextAnalyzeFileRequest、応答 ModuleData。</summary>
        public const string AnalyzeFile = "AnalyzeFile";

        /// <summary>文章を解析してモジュールのデータにする。要求 AITextAnalyzeTextRequest、応答 ModuleData。</summary>
        public const string AnalyzeText = "AnalyzeText";
    }
}
