using Microsoft.JSInterop;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    public class Logger : Codeer.LowCode.Blazor.RequestInterfaces.ILogger
    {
        readonly IJSRuntime _jsRuntime;
        readonly IToasterEx _toaster;

        public Logger(IJSRuntime js, IToasterEx toaster)
        {
            _jsRuntime = js;
            _toaster = toaster;
        }

        public async Task Log(string message)
        {
            await _jsRuntime.InvokeVoidAsync("console.log", message);
        }

        public async Task Warn(string message)
        {
            _toaster.Warn(message);
            await _jsRuntime.InvokeVoidAsync("console.warn", message);
        }

        public async Task Error(string message)
        {
            _toaster.Error(message);
            await _jsRuntime.InvokeVoidAsync("console.error", message);
        }
    }
}
