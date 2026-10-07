using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// タグ付け (タグを付けるモジュール 1 つにつき 1 つ。1 行 = あるレコードに付いたタグ 1 つ) の契約。
    /// 行はタグを付けたレコードの Id とタグ名だけを持つ (タグのマスタは持たない)。TagField はこのモジュールを子の一覧として持つ。
    /// </summary>
    [Designer(DisplayName = "$TagLinkContractField")]
    [ToolboxIcon(PackIconMaterialKind = "TagArrowRight")]
    public class TagLinkContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeRoleType = 1;

        public TagLinkContractFieldDesign() : base(typeof(TagLinkContractFieldDesign).FullName!) { }

        /// <summary>タグを付けたレコードの Id (IdField。本体の保存で CLB が入れる)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$TagLinkContractOwnerId")]
        public string OwnerId { get; set; } = nameof(OwnerId);

        /// <summary>タグ名 (TextField)。役割名が Name だとフィールド名 (FieldDesignBase.Name) と重なるので TagName。既定のフィールド名は Name。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$TagLinkContractTagName")]
        public string TagName { get; set; } = "Name";

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(OwnerId), nameof(TagName) };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule == null) return result;
            CheckRoleType(result, context, ownModule, nameof(OwnerId), OwnerId, "Id", e => e is IdFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(TagName), TagName, "Text", e => e is TextFieldDesign);
            return result;
        }

        //役割のフィールドの型。不在は ContractFieldDesignBase が指摘済みなので存在するものだけ見る
        void CheckRoleType(List<DesignCheckInfo> result, DesignCheckContext context, ModuleDesign ownModule,
            string roleName, string fieldName, string expected, Func<FieldDesignBase, bool> isValid)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var field = ownModule.Fields.FirstOrDefault(e => e.Name == fieldName);
            if (field == null || isValid(field)) return;
            result.Add(new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(typeof(TagLinkContractFieldDesign), CodeRoleType),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = roleName },
                Message = string.Format(Properties.Resources.TagCheck_RoleTypeFormat, roleName, fieldName, expected),
            });
        }
    }
}
