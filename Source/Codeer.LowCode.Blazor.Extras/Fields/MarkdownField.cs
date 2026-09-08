using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Markdown;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>Markdown を保持する値フィールド。Value は Markdown のテキストそのもの。</summary>
    public class MarkdownField(MarkdownFieldDesign design)
        : ValueFieldBase<MarkdownFieldDesign, MarkdownFieldData, string>(design)
    {
        /// <summary>現在の値を描画した HTML (生 HTML は無効化済み)。表示や他フィールドへの転記に使う。</summary>
        public string Html => MarkdownRenderer.ToHtml(Value);

        /// <summary>記法を落としたプレーンテキスト。一覧セルや検索用の要約に使う。</summary>
        public string PlainText => MarkdownRenderer.ToPlainText(Value);

        /// <summary>末尾に 1 行追加する (空なら最初の行になる)。スクリプトからの追記用。</summary>
        [ScriptName("AppendLine")]
        public async Task AppendLineAsync(string line)
        {
            var current = Value ?? string.Empty;
            var text = current.Length == 0 ? line : current.TrimEnd('\r', '\n') + "\n" + line;
            await SetValueAsync(text);
        }

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            if (Design.MaxLength is int max && (Value?.Length ?? 0) > max)
            {
                SetError(string.Format(Properties.Resources.MarkdownFieldMaxLengthError, max));
                return false;
            }
            return await base.ValidateInput();
        }
    }
}
