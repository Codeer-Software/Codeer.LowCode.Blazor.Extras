using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// タグのマスタ (タグ 1 つ = 1 行) の契約。TagField の候補はこのモジュールから読み、新しいタグはこのモジュールに行を足す。
    /// アプリに 1 つ置き、タグを付けるモジュールすべてで共有する。
    /// </summary>
    [Designer(DisplayName = "$TagContractField")]
    [ToolboxIcon(PackIconMaterialKind = "TagOutline")]
    public class TagContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeRoleType = 1;

        public TagContractFieldDesign() : base(typeof(TagContractFieldDesign).FullName!) { }

        /// <summary>タグ名 (Text)。役割名が Name だとフィールド名 (FieldDesignBase.Name) と重なるので TagName。既定のフィールド名は Name。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$TagContractTagName")]
        public string TagName { get; set; } = "Name";

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(TagName) };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule == null) return result;
            TagContractChecks.CheckRoleType(result, context, Name, typeof(TagContractFieldDesign), CodeRoleType, ownModule, nameof(TagName), TagName, "Text", e => e is TextFieldDesign);
            return result;
        }
    }

    /// <summary>
    /// タグ付け (タグを付けるモジュール 1 つにつき 1 つ。1 行 = あるレコードにあるタグが付いていること) の契約。
    /// CLB の多対多の形 (本体 ← IdField / LinkField → マスタ) そのもので、TagField はこのモジュールを子の一覧として持つ。
    /// </summary>
    [Designer(DisplayName = "$TagLinkContractField")]
    [ToolboxIcon(PackIconMaterialKind = "TagArrowRight")]
    public class TagLinkContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeRoleType = 1;
        private const int CodeTagTargetNotMaster = 2;
        private const int CodeTagDisplayText = 3;

        public TagLinkContractFieldDesign() : base(typeof(TagLinkContractFieldDesign).FullName!) { }

        /// <summary>タグを付けたレコードの Id (IdField。本体の保存で CLB が入れる)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$TagLinkContractOwnerId")]
        public string OwnerId { get; set; } = nameof(OwnerId);

        /// <summary>付けたタグ (LinkField → TagContractField を置いたマスタ)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$TagLinkContractTag")]
        public string Tag { get; set; } = nameof(Tag);

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(OwnerId), nameof(Tag) };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule == null) return result;
            TagContractChecks.CheckRoleType(result, context, Name, typeof(TagLinkContractFieldDesign), CodeRoleType, ownModule, nameof(OwnerId), OwnerId, "Id", e => e is IdFieldDesign);
            TagContractChecks.CheckRoleType(result, context, Name, typeof(TagLinkContractFieldDesign), CodeRoleType, ownModule, nameof(Tag), Tag, "Link", e => e is LinkFieldDesign);

            //Tag の先はタグのマスタ (TagContractField を置いたモジュール)
            if (ownModule.Fields.FirstOrDefault(e => e.Name == Tag) is LinkFieldDesign link)
            {
                var target = context.DesignData.Modules.Find(link.SearchCondition.ModuleName);
                var master = target?.Fields.OfType<TagContractFieldDesign>().FirstOrDefault();
                if (target != null && master == null)
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Code = DesignCheckCode.Create(typeof(TagLinkContractFieldDesign), CodeTagTargetNotMaster),
                        Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Tag) },
                        Message = string.Format(Properties.Resources.TagCheck_NotTagMasterFormat, target.Name),
                    });
                }
                //TagField はリンクの表示文字列をタグ名として使う
                var nameVariable = master == null || string.IsNullOrEmpty(master.TagName) ? string.Empty : $"{master.TagName}.Value";
                if (nameVariable.Length > 0 && link.DisplayTextVariable != nameVariable)
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Code = DesignCheckCode.Create(typeof(TagLinkContractFieldDesign), CodeTagDisplayText),
                        Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Tag) },
                        Message = string.Format(Properties.Resources.TagCheck_TagDisplayTextFormat, link.Name, nameVariable),
                    });
                }
            }
            return result;
        }
    }

    static class TagContractChecks
    {
        //役割のフィールドの型。不在は ContractFieldDesignBase が指摘済みなので存在するものだけ見る
        internal static void CheckRoleType(List<DesignCheckInfo> result, DesignCheckContext context, string contractFieldName,
            Type issuer, int code, ModuleDesign ownModule, string roleName, string fieldName, string expected, Func<FieldDesignBase, bool> isValid)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var field = ownModule.Fields.FirstOrDefault(e => e.Name == fieldName);
            if (field == null || isValid(field)) return;
            result.Add(new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(issuer, code),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = contractFieldName, Member = roleName },
                Message = string.Format(Properties.Resources.TagCheck_RoleTypeFormat, roleName, fieldName, expected),
            });
        }
    }
}
