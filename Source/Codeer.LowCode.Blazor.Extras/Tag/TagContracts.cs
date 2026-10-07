using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>TagField の結び付き。タグ付けモジュール (TagLinkContractField) の役割を、実際のフィールド名に解決したもの。</summary>
    internal sealed class TagBinding
    {
        /// <summary>タグ付けモジュール。</summary>
        public string LinkModule { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグを付けたレコードの Id のフィールド。</summary>
        public string OwnerIdField { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグ名のフィールド。</summary>
        public string TagNameField { get; init; } = string.Empty;
    }

    internal static class TagContracts
    {
        /// <summary>TagField の設定から結び付きを解決する。欠けていれば null (デザインチェックが指摘する不備)。</summary>
        internal static TagBinding? Resolve(DesignData design, TagFieldDesign field)
        {
            var linkModuleName = field.SearchCondition?.ModuleName ?? string.Empty;
            var link = LinkContract(design.Modules.Find(linkModuleName));
            if (link == null || string.IsNullOrEmpty(link.TagName)) return null;
            if (string.IsNullOrEmpty(OwnerKeyVariable(field.SearchCondition!, link.OwnerId))) return null;
            return new TagBinding
            {
                LinkModule = linkModuleName,
                OwnerIdField = link.OwnerId,
                TagNameField = link.TagName,
            };
        }

        internal static TagLinkContractFieldDesign? LinkContract(ModuleDesign? module)
            => module?.Fields.OfType<TagLinkContractFieldDesign>().FirstOrDefault();

        /// <summary>検索条件の中の「OwnerId.Value = (本体の変数)」から本体の変数を取り出す (無ければ空)。</summary>
        internal static string OwnerKeyVariable(SearchCondition condition, string ownerIdField)
            => Flatten(condition.Condition)
                .OfType<FieldVariableMatchCondition>()
                .FirstOrDefault(e => e.Comparison == MatchComparison.Equal && e.SearchTargetVariable == $"{ownerIdField}.Value")
                ?.Variable ?? string.Empty;

        static IEnumerable<MatchConditionBase> Flatten(MatchConditionBase? condition)
        {
            if (condition == null) yield break;
            yield return condition;
            var children = condition is MultiMatchCondition multi ? multi.Children : null;
            if (children == null) yield break;
            foreach (var child in children)
                foreach (var e in Flatten(child)) yield return e;
        }

        /// <summary>TagField のデータ (タグ付け行) のタグ名 (行の順)。</summary>
        internal static List<string> TagNames(DesignData? design, TagFieldDesign field, ListFieldData data)
        {
            var binding = design == null ? null : Resolve(design, field);
            return data.GetModules()
                .Select(row => binding != null
                    ? (row.Fields.GetValueOrDefault(binding.TagNameField) as TextFieldData)?.Value ?? string.Empty
                    : row.Fields.Values.OfType<TextFieldData>().FirstOrDefault()?.Value ?? string.Empty)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();
        }
    }
}
