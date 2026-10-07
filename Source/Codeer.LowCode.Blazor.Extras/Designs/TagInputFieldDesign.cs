using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// タグを入力するだけで保存しない欄 (値はタグ名のリスト。メモリに持つ)。取り込み画面・一括でタグを付ける画面で「付けるタグ」を選ぶ欄など。
    /// 入力部品と候補の設定は TagField と同じ。保存するタグは <see cref="TagFieldDesign"/>。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TagPlusOutline")]
    [Designer(DisplayName = "$TagInputField")]
    [IgnoreBaseProperties(nameof(IgnoreModification))]
    public class TagInputFieldDesign() : ValueFieldDesignBase(typeof(TagInputFieldDesign).FullName!), ITagCandidateDesign
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeCandidateModuleRequired = 1;
        private const int CodeCandidateFieldType = 2;
        private const int CodeCandidateValuesRequired = 3;
        private const int CodeTagRowsNotAvailable = 4;

        [Designer(Index = 3, DisplayName = "$TagFieldPlaceholder")]
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>スペースでもタグを確定する。既定は Enter と「,」「、」だけ。</summary>
        [Designer(Index = 4, DisplayName = "$TagFieldConfirmOnSpace")]
        public bool ConfirmOnSpace { get; set; }

        /// <summary>候補に無いタグを入力できる。false なら候補にあるタグだけ (足すときに確かめる)。</summary>
        [Designer(Index = 5, DisplayName = "$TagFieldAllowNewTags")]
        public bool AllowNewTags { get; set; } = true;

        /// <summary>候補の出どころ (Module か Values。この欄はタグ付けモジュールを持たないので TagRows は使えない)。</summary>
        [Designer(Index = 6, DisplayName = "$TagFieldCandidateSource")]
        public TagCandidateSource CandidateSource { get; set; } = TagCandidateSource.Module;

        /// <summary>CandidateSource = Module のときの、候補を読むモジュール。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Module, DisplayName = "$TagFieldCandidateModuleName")]
        public string CandidateModuleName { get; set; } = string.Empty;

        /// <summary>CandidateSource = Module のときの、候補を読むフィールド (TextField か TagField)。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$TagFieldCandidateFieldName"), ModuleMember(Member = nameof(CandidateModuleName))]
        public string CandidateFieldName { get; set; } = string.Empty;

        /// <summary>CandidateSource = Values のときの決まったタグ (1 行 1 つ。並びのまま出す)。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.MultilineString, DisplayName = "$TagFieldCandidateValues")]
        public string CandidateValues { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(TagInputFieldComponent).FullName!;

        //検索には使わない (保存しないので探すものが無い)
        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new TagInputField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldName(Name).AddTo(result);
            if (CandidateSource == TagCandidateSource.TagRows)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TagInputFieldDesign), CodeTagRowsNotAvailable),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(CandidateSource) },
                    Message = Properties.Resources.TagCheck_TagRowsNeedsTagField,
                });
            }
            TagCandidateChecks.Check(context, result, this, typeof(TagInputFieldDesign), CodeCandidateModuleRequired, CodeCandidateFieldType, CodeCandidateValuesRequired);
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(CandidateModuleName, x => CandidateModuleName = x)
            .AddField(CandidateModuleName, CandidateFieldName, x => CandidateFieldName = x)
            .Build();
    }
}
