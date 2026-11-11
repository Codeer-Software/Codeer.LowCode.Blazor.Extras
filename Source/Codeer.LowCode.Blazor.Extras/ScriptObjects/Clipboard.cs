using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// スクリプトから文字列をクリップボードにコピーする (スクリプトでは <c>Clipboard.Copy("...")</c>)。
    /// 書き込みはブラウザーの navigator.clipboard.writeText を使うので、HTTPS か localhost で、
    /// ボタンのクリックなどユーザー操作の中から呼んだときだけ成功する。
    /// 通知は出さない (結果の bool を見てスクリプト側で Toaster などを使う)。ClipboardCopyButtonField もこれで書き込む。
    /// </summary>
    public class Clipboard
    {
        [ScriptHide, ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        /// <summary>文字列をクリップボードにコピーする。成功なら true。拒否された (HTTPS でない / 権限が無い / ページにフォーカスが無い) ときは false。</summary>
        [ScriptName("Copy")]
        public async Task<bool> CopyAsync(string text)
        {
            if (Services == null || Services.AppInfoService.IsDesignMode) return false;
            var js = Services.Provider.GetService<IJSRuntime>();
            if (js == null) return false;
            try
            {
                await js.InvokeVoidAsync("navigator.clipboard.writeText", text ?? string.Empty);
                return true;
            }
            catch (JSException)
            {
                return false;
            }
        }
    }
}
