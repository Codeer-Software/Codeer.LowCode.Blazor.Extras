namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>AI チャットが共通の API (ExtrasTransport / Extras.Server の AIChatApi) で使う機能名と操作名。</summary>
    public static class AIChatApiNames
    {
        public const string Feature = "AIChat";

        /// <summary>発言を送って返事のジョブを起動する。要求 AIChatSendRequest、応答 AIChatSendResponse。</summary>
        public const string Send = "Send";

        /// <summary>返事のジョブの状態。要求 AIChatJobRequest、応答 AIChatStatusResponse (知らない Id・他のユーザーのジョブは応答なし)。</summary>
        public const string Status = "Status";

        /// <summary>返事のジョブの中断。要求 AIChatJobRequest、応答なし。</summary>
        public const string Cancel = "Cancel";
    }
}
