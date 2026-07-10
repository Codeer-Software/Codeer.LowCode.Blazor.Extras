using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class WebApiResult
    {
        public JsonObject JsonObject { get; set; } = new();
        public int StatusCode { get; set; }
    }
}
