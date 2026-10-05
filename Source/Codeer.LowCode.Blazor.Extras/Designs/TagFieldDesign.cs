using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>TagField の検索で、選んだタグをどう組み合わせるか。</summary>
    public enum TagSearchMatch
    {
        /// <summary>選んだタグをすべて含む行。</summary>
        [Designer(DisplayName = "$TagSearchMatch_All")] All,
        /// <summary>選んだタグのどれかを含む行。</summary>
        [Designer(DisplayName = "$TagSearchMatch_Any")] Any,
    }

    /// <summary>
    /// タグを入力・表示する値フィールド。値はタグを ", " でつないだ文字列のまま DB 列に保存し、画面ではチップで表示する。
    /// 候補は既に付いているタグ (既定は同じモジュールの同じ列)。検索は選んだタグごとの部分一致 (本体の Like)。
    /// 検索できる DB 列のフィールドなので、本体の TextField と同じ DbValueFieldDesignBase を継承する。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TagMultiple")]
    [Designer(DisplayName = "$TagField")]
    public class TagFieldDesign() : DbValueFieldDesignBase(typeof(TagFieldDesign).FullName!)
    {
        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$TagFieldDbColumn"), DbColumn(nameof(TagFieldData.Value))]
        public override string DbColumn { get; set; } = string.Empty;

        [Designer(Index = 1, DisplayName = "$TagFieldPlaceholder")]
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>スペースでもタグを確定する。既定は Enter と「,」「、」だけ (日本語のタグにはスペースが入ることがあるため)。</summary>
        [Designer(Index = 2, DisplayName = "$TagFieldConfirmOnSpace")]
        public bool ConfirmOnSpace { get; set; }

        /// <summary>タグが 1 つも無いときに保存する値 (本体 TextField の TextEditEmptyType と同じ。NOT NULL の列には StringEmpty)。</summary>
        [Designer(Index = 3, DisplayName = "$TagFieldTextEditEmptyType")]
        public TextEditEmptyType TextEditEmptyType { get; set; } = TextEditEmptyType.StringEmpty;

        /// <summary>候補を読むモジュール。空ならこのフィールドの列から読む (テーブルを持たない画面で、別のモジュールのタグを候補にするとき)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Module, DisplayName = "$TagFieldCandidateModuleName")]
        public string CandidateModuleName { get; set; } = string.Empty;

        /// <summary>候補を読むフィールド (CandidateModuleName の TagField か TextField)。空ならこのフィールドと同じ名前。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$TagFieldCandidateFieldName"), ModuleMember(Member = nameof(CandidateModuleName))]
        public string CandidateFieldName { get; set; } = string.Empty;

        /// <summary>検索画面の一致の既定 (画面で切り替えられる。本体 TextField の SearchComparisonDefaultValue と同じ扱い)。</summary>
        [Designer(DisplayName = "$TagFieldSearchMatchDefaultValue", Category = "$SearchSettings")]
        public TagSearchMatch SearchMatchDefaultValue { get; set; } = TagSearchMatch.All;

        public override string GetWebComponentTypeFullName() => typeof(TagFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => typeof(TagFieldSearchComponent).FullName!;

        //デザイナの条件の編集は本体 TextField のものを使う (LinkField も同じものを使っている)
        public override string GetSearchControlTypeFullName() => "Codeer.LowCode.Blazor.Designer.Views.Controls.MatchItemViews.TextSearchControl";

        public override FieldBase CreateField() => new TagField(this);

        public override FieldDataBase? CreateData() => new TagFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            //DB 列・OnDataChanged・OnSearchDataChanged の存在は基底が見る
            var result = base.CheckDesign(context);
            context.CheckFieldName(Name).AddTo(result);
            if (!string.IsNullOrEmpty(CandidateModuleName))
            {
                context.CheckFieldModuleExistence(Name, nameof(CandidateModuleName), CandidateModuleName).AddTo(result);
                context.CheckFieldRelativeFieldExistence(Name, nameof(CandidateFieldName), CandidateModuleName, CandidateFieldName).AddTo(result);
            }
            return result;
        }

        //本体 TextField と同じ規則: 比較は Like / Equal、空検索 (null / "" との Equal / NotEqual) は AllowEmptySearch のときだけ
        public override string ValidateSearchCondition(IFieldMatchCondition condition)
        {
            if (condition is FieldValueMatchCondition value && IsEmptyValue(value.Value) &&
                value.Comparison is MatchComparison.Equal or MatchComparison.NotEqual)
            {
                return AllowEmptySearch ? string.Empty : string.Format(Properties.Resources.TagFieldEmptySearchNotAllowed, Name);
            }
            return condition.Comparison is MatchComparison.Like or MatchComparison.Equal
                ? string.Empty
                : string.Format(Properties.Resources.TagFieldSearchComparisonNotAvailable, Name, condition.Comparison);
        }

        static bool IsEmptyValue(MultiTypeValue? value) => value is null or NullValue || value is StringValue { Value: null or "" };

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(CandidateModuleName, x => CandidateModuleName = x)
            .AddField(CandidateModuleName, CandidateFieldName, x => CandidateFieldName = x)
            .Build();
    }
}
