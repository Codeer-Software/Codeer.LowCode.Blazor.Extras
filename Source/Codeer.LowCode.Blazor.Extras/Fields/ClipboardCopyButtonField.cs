using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 押すと文字列をクリップボードにコピーするボタン。
    /// コピーする文字列はコピー元フィールド (TextField の値そのまま) か固定文字列から取る。
    /// クリップボードへの書き込みはスクリプトの Clipboard と同じ (ブラウザーの navigator.clipboard.writeText。HTTPS か localhost で、クリック操作の中でのみ動く)。
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

        /// <summary>
        /// デザインで指定したコピー元 (コピー元フィールド / コピーする文字列) の文字列をクリップボードにコピーする。
        /// 成功したら通知を出して OnCopied を呼ぶ。成功なら true。
        /// 任意の文字列をコピーしたいときはスクリプトの Clipboard.Copy を使う。
        /// </summary>
        [ScriptName("Copy")]
        public async Task<bool> CopyAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isCopying) return false;
            return await CopyCoreAsync(GetSourceText());
        }

        async Task<bool> CopyCoreAsync(string text)
        {
            _isCopying = true;
            NotifyStateChanged();
            try
            {
                //書き込みはスクリプトの Clipboard と共通
                if (!await new ScriptObjects.Clipboard { Services = Services }.CopyAsync(text))
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

        /// <summary>
        /// コピー元フィールド (TextField) があればその値をそのまま、無ければ固定文字列を返す。
        /// フィールドの表示をボタンの側で組み立て直すことはしない (表示とずれるため)。
        /// </summary>
        string GetSourceText()
        {
            if (string.IsNullOrEmpty(Design.SourceField)) return Design.CopyText;
            return (Module?.GetField(Design.SourceField) as TextField)?.Value ?? string.Empty;
        }
    }
}
