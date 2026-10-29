namespace Codeer.LowCode.Blazor.Extras.Api
{
    /// <summary>
    /// <see cref="ExtrasRequest"/> への応答。操作ごとの応答 (MailSendResult 等) を Payload に JSON で入れる。
    /// </summary>
    public class ExtrasResponse
    {
        /// <summary>操作ごとの応答の JSON。返すものが無い操作 (知らないジョブ Id の状態など) は空。</summary>
        public string Payload { get; set; } = string.Empty;
    }
}
