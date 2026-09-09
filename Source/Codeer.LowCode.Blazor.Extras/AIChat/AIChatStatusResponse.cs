namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>
    /// ジョブの状態 (GET {EndPoint}/{requestId})。running 中の <see cref="Reply"/> は「ここまでの HTML」(逐次表示用。空でもよい)。
    /// </summary>
    public class AIChatStatusResponse
    {
        public string Status { get; set; } = AIChatJobStatus.Running;
        /// <summary>返事の HTML。running 中は途中経過 (任意)、done で確定。</summary>
        public string Reply { get; set; } = string.Empty;
        /// <summary>途中経過の一言 (「検索しています…」等。任意)。</summary>
        public string Progress { get; set; } = string.Empty;
        /// <summary>error のときのメッセージ。</summary>
        public string Error { get; set; } = string.Empty;

        public bool IsRunning => Status == AIChatJobStatus.Running;
    }
}
