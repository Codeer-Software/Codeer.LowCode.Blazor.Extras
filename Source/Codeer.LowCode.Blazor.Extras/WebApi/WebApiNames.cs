namespace Codeer.LowCode.Blazor.Extras.WebApi
{
    /// <summary>WebApiField が共通の API (ExtrasTransport / Extras.Server の WebApiApi) で使う機能名と操作名。</summary>
    public static class WebApiNames
    {
        public const string Feature = "WebApi";

        /// <summary>外部の Web API の呼び出し。要求 WebApiExecuteRequest、応答 WebApiExecuteResponse。</summary>
        public const string Execute = "Execute";
    }
}
