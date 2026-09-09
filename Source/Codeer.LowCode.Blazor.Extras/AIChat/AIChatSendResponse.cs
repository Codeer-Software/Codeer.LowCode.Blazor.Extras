namespace Codeer.LowCode.Blazor.Extras.AIChat
{
    /// <summary>送信の応答。以後 GET {EndPoint}/{RequestId} で <see cref="AIChatStatusResponse"/> を取る。</summary>
    public class AIChatSendResponse
    {
        public string RequestId { get; set; } = string.Empty;
    }
}
