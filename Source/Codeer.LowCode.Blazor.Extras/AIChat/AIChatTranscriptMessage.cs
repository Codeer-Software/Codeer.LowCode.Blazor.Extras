namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>
    /// クライアントが持っている会話の写し 1 件 (テキストのみ)。<see cref="AIChatSendRequest.Transcript"/> で送る。
    /// サーバーの会話履歴 (会話 ID ごと・メモリ・保持期限あり) が消えていたとき、Agent が文脈を取り戻すためだけに使う。
    /// 履歴が残っていれば無視される。ツールの結果 (SQL の結果等) は含まないので、消えた後の追問は精度が落ちる。
    /// </summary>
    public class AIChatTranscriptMessage
    {
        public bool IsUser { get; set; }
        /// <summary>発言のテキスト (返事は HTML からタグを除いたもの)。</summary>
        public string Text { get; set; } = string.Empty;
    }
}
