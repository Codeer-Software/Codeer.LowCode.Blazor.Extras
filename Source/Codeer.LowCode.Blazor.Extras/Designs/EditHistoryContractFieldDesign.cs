using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 編集履歴モジュールの契約。対象モジュール (EditHistoryField を置いたモジュール) の保存 1 回につき
    /// 1 行、レコード全体のスナップショット (JSON) が書かれる。
    /// 必須の役割はサーバーの記録と閲覧 UI が「どのレコードのどの版か」を引くために読むもの。
    /// UserId / DateTime は空にできる (= 記録しない)。
    /// </summary>
    [Designer(DisplayName = "$EditHistoryContractField")]
    [ToolboxIcon(PackIconMaterialKind = "History")]
    public class EditHistoryContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeRoleType = 1;
        private const int CodeTargetEnumMember = 2;

        public EditHistoryContractFieldDesign() : base(typeof(EditHistoryContractFieldDesign).FullName!) { }

        /// <summary>対象モジュール名 (Text / Select)。Select で enum を指せば一覧の表示と検索をモジュール名から画面上の名前に読み替えられる (enum は任意)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractModuleName")]
        public string ModuleName { get; set; } = nameof(ModuleName);

        /// <summary>対象レコードの Id (Text)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractDataId")]
        public string DataId { get; set; } = nameof(DataId);

        /// <summary>変更種別 (Text または Select。値は EditHistoryChangeType のメンバー名)。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractChangeType")]
        public string ChangeType { get; set; } = nameof(ChangeType);

        /// <summary>レコード全体のスナップショット JSON (Text)。削除は削除前、作成・更新は保存後の内容。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractSnapshot")]
        public string Snapshot { get; set; } = nameof(Snapshot);

        /// <summary>変更したユーザー (Link→ユーザーモジュール、または Text)。空 = 記録しない。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractUserId")]
        public string UserId { get; set; } = nameof(UserId);

        /// <summary>変更日時 (DateTime)。空 = 記録しない。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$EditHistoryContractDateTime")]
        public string DateTime { get; set; } = nameof(DateTime);

        private protected override HashSet<string> RequiredRoleNames => new()
        {
            nameof(ModuleName), nameof(DataId), nameof(ChangeType), nameof(Snapshot),
        };

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule == null) return result;

            //役割のフィールド型 (記録側が書く型)。不在は base が指摘済みなので存在するものだけ見る
            CheckRoleType(result, context, ownModule, nameof(ModuleName), ModuleName, "Text / Select", e => e is TextFieldDesign or SelectFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(DataId), DataId, "Text", e => e is TextFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(ChangeType), ChangeType, "Text / Select", e => e is TextFieldDesign or SelectFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(Snapshot), Snapshot, "Text", e => e is TextFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(UserId), UserId, "Link / Text", e => e is LinkFieldDesign or TextFieldDesign);
            CheckRoleType(result, context, ownModule, nameof(DateTime), DateTime, "DateTime", e => e is DateTimeFieldDesign);
            CheckTargetEnumMembers(result, context, ownModule);
            return result;
        }

        //ModuleName が enum 付きの Select なら、この履歴モジュールに記録するモジュールが enum に無いと一覧で読み替えられない。
        //enum が無い・空なら「読み替え無しで運用」とみなして指摘しない (Select は候補に無い値もそのまま表示する)
        void CheckTargetEnumMembers(List<DesignCheckInfo> result, DesignCheckContext context, ModuleDesign ownModule)
        {
            if (ownModule.Fields.FirstOrDefault(e => e.Name == ModuleName) is not SelectFieldDesign select || string.IsNullOrEmpty(select.EnumName)) return;
            var enumDesign = context.DesignData.Enums.FirstOrDefault(e => e.Name == select.EnumName);
            if (enumDesign == null || enumDesign.Members.Count == 0) return;
            var missing = context.DesignData.Modules.ToList()
                .Where(m => m.Fields.OfType<EditHistoryFieldDesign>().Any(f => f.HistoryModuleName == ownModule.Name))
                .Select(m => m.Name)
                .Where(name => enumDesign.FindMemberByValue(name) == null);
            foreach (var name in missing)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryContractFieldDesign), CodeTargetEnumMember),
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(ModuleName) },
                    Message = string.Format(Properties.Resources.EditHistoryCheck_TargetEnumMemberMissingFormat, name, select.EnumName),
                });
            }
        }

        void CheckRoleType(List<DesignCheckInfo> result, DesignCheckContext context, ModuleDesign ownModule,
            string roleName, string fieldName, string expected, Func<FieldDesignBase, bool> isValid)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            var field = ownModule.Fields.FirstOrDefault(e => e.Name == fieldName);
            if (field == null || isValid(field)) return;
            result.Add(new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(typeof(EditHistoryContractFieldDesign), CodeRoleType),
                Location = new FieldDesignDataLocation
                { Module = context.OwnerModule, Field = Name, Member = roleName },
                Message = string.Format(Properties.Resources.EditHistoryCheck_RoleTypeFormat, roleName, fieldName, expected),
            });
        }
    }
}
