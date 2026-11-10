using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class WebApiResult
    {
        public JsonObject JsonObject { get; set; } = new();
        public int StatusCode { get; set; }

        /// <summary>StatusCode が 200〜299。</summary>
        public bool IsSuccess => 200 <= StatusCode && StatusCode <= 299;

        /// <summary>呼び出せなかったとき (通信できない・設定が無い等) の説明。既定は空。</summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>応答の本文そのまま。無ければ空文字列。</summary>
        public string Body { get; set; } = string.Empty;
    }
}
