using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 押すと文字列をクリップボードにコピーするボタン。
    /// コピーする文字列は、同じモジュールの別フィールド (<see cref="SourceField"/>) か固定文字列 (<see cref="CopyText"/>) のどちらか一方で指定する。
    /// コピー元に置けるのは MarkupStringField (HTML をテキストにしてコピー) / LabelField / TextField。
    /// 値を持たないフィールド。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "ContentCopy")]
    [Designer(DisplayName = "$ClipboardCopyButtonField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class ClipboardCopyButtonFieldDesign() : FieldDesignBase(typeof(ClipboardCopyButtonFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeSourceInvalid = 1;

        /// <summary>ボタンの文字。空なら既定 ("コピー")。</summary>
        [Designer(Index = 1, DisplayName = "$ClipboardCopyButtonText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>ボタンの見た目 (Bootstrap のボタンの種類)。</summary>
        [Designer(Index = 2, DisplayName = "$ClipboardCopyButtonVariant")]
        public ButtonVariant Variant { get; set; } = ButtonVariant.OutlineSecondary;

        /// <summary>コピー元フィールド名 (同じモジュールの MarkupStringField / LabelField / TextField)。<see cref="CopyText"/> とどちらか一方だけを設定する。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$ClipboardCopyButtonSourceField")]
        [TargetFieldType(Types = [typeof(MarkupStringFieldDesign), typeof(LabelFieldDesign), typeof(TextFieldDesign)])]
        public string SourceField { get; set; } = string.Empty;

        /// <summary>コピーする固定文字列。<see cref="SourceField"/> とどちらか一方だけを設定する。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.MultilineString, DisplayName = "$ClipboardCopyButtonCopyText")]
        public string CopyText { get; set; } = string.Empty;

        /// <summary>コピーできたときの通知メッセージ。空なら既定 ("コピーしました")。</summary>
        [Designer(Index = 5, DisplayName = "$ClipboardCopyButtonCopiedMessage")]
        public string CopiedMessage { get; set; } = string.Empty;

        /// <summary>コピーできた後に呼ばれるスクリプトイベント。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.ScriptEvent, DisplayName = "$ClipboardCopyButtonOnCopied")]
        public string OnCopied { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(ClipboardCopyButtonFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new ClipboardCopyButtonField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            //コピー元 (コピー元フィールド/コピーする文字列) はどちらか一方だけ設定する
            if (string.IsNullOrEmpty(SourceField) == string.IsNullOrEmpty(CopyText))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(ClipboardCopyButtonFieldDesign), CodeSourceInvalid),
                    Location = new() { Module = context.OwnerModule, Field = Name },
                    Message = Properties.Resources.ClipboardCopyButtonSourceInvalid
                });
            }

            context.CheckFieldFieldExistence(Name, nameof(SourceField), SourceField).AddTo(result);
            context.CheckFieldFieldInstanceType(Name, nameof(SourceField), SourceField,
                typeof(MarkupStringFieldDesign), typeof(LabelFieldDesign), typeof(TextFieldDesign)).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnCopied), OnCopied,
                context.GetScriptMethodAttribute(GetType(), nameof(OnCopied))).AddTo(result);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(SourceField, x => SourceField = x)
            .Build();
    }
}
