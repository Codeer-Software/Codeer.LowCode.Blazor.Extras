using System.Text;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 押すと文字列をクリップボードにコピーするボタン。
    /// コピーする文字列はコピー元フィールド (MarkupStringField / LabelField / TextField) か固定文字列から取る。
    /// クリップボードへの書き込みはブラウザーの navigator.clipboard.writeText を使う (HTTPS か localhost で、クリック操作の中でのみ動く)。
    /// </summary>
    public class ClipboardCopyButtonField(ClipboardCopyButtonFieldDesign design)
        : FieldBase<ClipboardCopyButtonFieldDesign>(design)
    {
        bool _isCopying;

        //値を持たないフィールド
        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        /// <summary>コピー実行中か (ボタンの二重実行防止)。</summary>
        internal bool IsCopying => _isCopying;

        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? Properties.Resources.ClipboardCopyButton_DefaultText : Design.Text;

        [ScriptHide]
        public string ButtonClass => "btn " + Design.Variant switch
        {
            ButtonVariant.Primary => "btn-primary",
            ButtonVariant.Secondary => "btn-secondary",
            ButtonVariant.Success => "btn-success",
            ButtonVariant.Danger => "btn-danger",
            ButtonVariant.Warning => "btn-warning",
            ButtonVariant.Info => "btn-info",
            ButtonVariant.Light => "btn-light",
            ButtonVariant.Dark => "btn-dark",
            ButtonVariant.Link => "btn-link",
            ButtonVariant.Text => string.Empty,
            ButtonVariant.OutlinePrimary => "btn-outline-primary",
            ButtonVariant.OutlineSuccess => "btn-outline-success",
            ButtonVariant.OutlineDanger => "btn-outline-danger",
            ButtonVariant.OutlineWarning => "btn-outline-warning",
            ButtonVariant.OutlineInfo => "btn-outline-info",
            ButtonVariant.OutlineLight => "btn-outline-light",
            ButtonVariant.OutlineDark => "btn-outline-dark",
            _ => "btn-outline-secondary",
        };

        /// <summary>
        /// デザインで指定したコピー元 (コピー元フィールド / コピーする文字列) の文字列をクリップボードにコピーする。
        /// 成功したら通知を出して OnCopied を呼ぶ。成功なら true。
        /// </summary>
        [ScriptName("Copy")]
        public async Task<bool> CopyAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isCopying) return false;
            return await CopyCoreAsync(await GetSourceTextAsync());
        }

        /// <summary>指定した文字列をクリップボードにコピーする (コピー元の設定は使わない)。成功なら true。</summary>
        [ScriptName("CopyText")]
        public async Task<bool> CopyTextAsync(string text)
        {
            if (Services.AppInfoService.IsDesignMode || _isCopying) return false;
            return await CopyCoreAsync(text ?? string.Empty);
        }

        async Task<bool> CopyCoreAsync(string text)
        {
            var js = Services.Provider.GetService<IJSRuntime>();
            if (js == null) return false;

            _isCopying = true;
            NotifyStateChanged();
            try
            {
                try
                {
                    await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
                }
                catch (JSException)
                {
                    //HTTPS でない / 権限が拒否された / ページにフォーカスが無い など
                    await Services.UIService.NotifyError(Properties.Resources.ClipboardCopyButton_Failed);
                    return false;
                }

                await Services.UIService.NotifySuccess(string.IsNullOrEmpty(Design.CopiedMessage)
                    ? Properties.Resources.ClipboardCopyButton_Copied : Design.CopiedMessage);
                if (!string.IsNullOrEmpty(Design.OnCopied)) await Module!.ExecuteScriptAsync(Design.OnCopied);
                return true;
            }
            finally
            {
                _isCopying = false;
                NotifyStateChanged();
            }
        }

        /// <summary>コピー元フィールドがあればその表示内容、無ければ固定文字列を返す。</summary>
        async Task<string> GetSourceTextAsync()
        {
            if (string.IsNullOrEmpty(Design.SourceField)) return Design.CopyText;

            switch (Module?.GetField(Design.SourceField))
            {
                case MarkupStringField markup:
                    //MarkupStringField と同じく HTML の直書きが優先、無ければリソースの HTML
                    if (!string.IsNullOrEmpty(markup.RawHtml)) return AIChatConversation.HtmlToText(markup.RawHtml);
                    if (string.IsNullOrEmpty(markup.ResourcePath)) return string.Empty;
                    using (var stream = await Services.AppInfoService.GetResourceAsync(markup.ResourcePath))
                    {
                        if (stream == null) return string.Empty;
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        return AIChatConversation.HtmlToText(await reader.ReadToEndAsync());
                    }
                case LabelField label:
                    return label.Design.IsHtml ? AIChatConversation.HtmlToText(label.Text) : label.Text;
                case TextField text:
                    return text.Value ?? string.Empty;
                default:
                    return string.Empty;
            }
        }
    }
}
