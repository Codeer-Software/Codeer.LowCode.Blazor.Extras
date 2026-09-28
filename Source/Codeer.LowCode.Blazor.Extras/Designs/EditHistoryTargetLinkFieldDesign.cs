using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 履歴行の対象レコード (ModuleName / DataId) を開くリンク。履歴モジュール (EditHistoryContractField を置いたモジュール) の
    /// 一覧の列や詳細に置く。対象モジュールが無い行・削除の版 (レコードはもう開けない) では出ない。
    /// 履歴モジュールは通常「誰も書けない」= 行が表示専用になるが、遷移するだけなので表示専用でも使える。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "OpenInNew")]
    [Designer(DisplayName = "$EditHistoryTargetLinkField")]
    public class EditHistoryTargetLinkFieldDesign : FieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeContractFieldMissing = 1;

        public EditHistoryTargetLinkFieldDesign() : base(typeof(EditHistoryTargetLinkFieldDesign).FullName!) { }

        /// <summary>リンクの文言。空なら既定 (「開く」)。</summary>
        [Designer(Index = 2, DisplayName = "$EditHistoryTargetLinkText")]
        public string Text { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(EditHistoryTargetLinkFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new EditHistoryTargetLinkField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && EditHistoryContracts.Contract(ownModule) == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryTargetLinkFieldDesign), CodeContractFieldMissing),
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = string.Format(Properties.Resources.ApprovalCheck_ContractFieldMissingFormat,
                        context.OwnerModule, nameof(EditHistoryContractFieldDesign)),
                });
            }
            return result;
        }
    }
}
