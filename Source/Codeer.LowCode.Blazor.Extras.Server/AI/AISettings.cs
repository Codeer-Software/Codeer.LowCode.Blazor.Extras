namespace Codeer.LowCode.Blazor.Extras.Server.AI
{
    public class AISettings
    {
        public string OpenAIEndPoint { get; set; } = string.Empty;
        public string OpenAIKey { get; set; } = string.Empty;
        public string ChatModel { get; set; } = string.Empty;
        public string DocumentAnalysisEndPoint { get; set; } = string.Empty;
        public string DocumentAnalysisKey { get; set; } = string.Empty;

        /// <summary>
        /// AITextAnalyzer: 抽出結果を JSON スキーマで受け取る (キーをフィールド名に固定)。モデルが表示名をキーにして値が捨てられるのを防ぐ。
        /// スキーマ指定の呼び出しが失敗したら従来の JSON モードでやり直すので、対応していないモデルでも動く。
        /// </summary>
        public bool TextAnalyzeJsonSchema { get; set; } = true;

        /// <summary>
        /// AITextAnalyzer: 解析するファイルが画像 (jpg / png / gif / webp) のとき、文書解析 (OCR) の結果に加えて画像そのものもモデルに渡す。
        /// OCR が落とす記号 (&lt; ≤ ≥ や下線)・小数点・似た形の文字をモデルが画像で確かめられる。画像の分だけトークンが増える。
        /// 画像を受け取れないモデルなら呼び出しが失敗し、従来の形 (OCR の結果だけ) でやり直す。
        /// </summary>
        public bool TextAnalyzeSendImage { get; set; } = true;
    }
}
