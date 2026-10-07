using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>タグの候補をどこから出すか (TagField / TagInputField 共通)。</summary>
    public enum TagCandidateSource
    {
        /// <summary>このフィールドのタグ付けモジュールに付いているタグ (よく使われている順)。TagField だけ。</summary>
        [Designer(DisplayName = "$TagCandidateSource_TagRows")] TagRows,
        /// <summary>指定したモジュールの列の値 (よく使われている順)。列が TagField なら、そのタグ付けモジュールのタグ。</summary>
        [Designer(DisplayName = "$TagCandidateSource_Module")] Module,
        /// <summary>設定に書いた決まったタグ (CandidateValues。1 行 1 つ)。</summary>
        [Designer(DisplayName = "$TagCandidateSource_Values")] Values,
    }

    /// <summary>候補の設定 (TagField / TagInputField 共通)。</summary>
    public interface ITagCandidateDesign
    {
        string Name { get; }
        TagCandidateSource CandidateSource { get; }
        string CandidateModuleName { get; }
        string CandidateFieldName { get; }
        string CandidateValues { get; }
        bool AllowNewTags { get; }
    }

    static class TagCandidateChecks
    {
        /// <summary>候補の設定のデザインチェック。Module なら モジュール・列 (TextField か TagField)、Values なら 1 つ以上。</summary>
        internal static void Check(DesignCheckContext context, List<DesignCheckInfo> result, ITagCandidateDesign design, Type issuer,
            int codeModuleRequired, int codeFieldType, int codeValuesRequired)
        {
            FieldDesignCheckInfo Error(int code, string member, string message) => new()
            {
                Code = DesignCheckCode.Create(issuer, code),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = design.Name, Member = member },
                Message = message,
            };

            switch (design.CandidateSource)
            {
                case TagCandidateSource.Module:
                    if (string.IsNullOrEmpty(design.CandidateModuleName) || string.IsNullOrEmpty(design.CandidateFieldName))
                    {
                        result.Add(Error(codeModuleRequired, nameof(ITagCandidateDesign.CandidateModuleName), Properties.Resources.TagCheck_CandidateModuleRequired));
                        return;
                    }
                    context.CheckFieldModuleExistence(design.Name, nameof(ITagCandidateDesign.CandidateModuleName), design.CandidateModuleName).AddTo(result);
                    context.CheckFieldRelativeFieldExistence(design.Name, nameof(ITagCandidateDesign.CandidateFieldName), design.CandidateModuleName, design.CandidateFieldName).AddTo(result);
                    var field = context.DesignData.Modules.Find(design.CandidateModuleName)?.Fields.FirstOrDefault(e => e.Name == design.CandidateFieldName);
                    if (field != null && field is not TextFieldDesign && field is not TagFieldDesign)
                        result.Add(Error(codeFieldType, nameof(ITagCandidateDesign.CandidateFieldName),
                            string.Format(Properties.Resources.TagCheck_CandidateFieldTypeFormat, design.CandidateModuleName, design.CandidateFieldName)));
                    break;
                case TagCandidateSource.Values:
                    if (ParseValues(design.CandidateValues).Count == 0)
                        result.Add(Error(codeValuesRequired, nameof(ITagCandidateDesign.CandidateValues), Properties.Resources.TagCheck_CandidateValuesRequired));
                    break;
            }
        }

        /// <summary>CandidateValues を 1 行 1 つのタグに (前後の空白・空行・重複は落とす。重複は大文字小文字を区別しない)。</summary>
        internal static List<string> ParseValues(string? values)
            => (values ?? string.Empty).Split('\n')
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
    }
}
