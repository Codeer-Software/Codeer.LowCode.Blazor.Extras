using Codeer.LowCode.Blazor.DesignLogic;
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
    /// 承認履歴表示フィールド。同一モジュール上の ApprovalFlowField を指定し、その履歴だけを表示する。
    /// 標準 UI から履歴の位置だけを切り離してレイアウトしたいときに使う (本体側は「履歴を表示」を OFF にする)。
    /// 状態は ApprovalFlowField が単一保持し、このフィールドは表示部品 (データ・送信なし)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "History")]
    [Designer(DisplayName = "$ApprovalHistoryField")]
    public class ApprovalHistoryFieldDesign : FieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        public static class Codes
        {
            public const int NotApprovalFlowField = 1;
        }

        public ApprovalHistoryFieldDesign() : base(typeof(ApprovalHistoryFieldDesign).FullName!) { }

        /// <summary>表示元の承認フローフィールド名。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$ApprovalFieldName")]
        public string ApprovalFieldName { get; set; } = "Approval";

        public override string GetWebComponentTypeFullName() => typeof(ApprovalHistoryFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new ApprovalHistoryField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            context.CheckFieldFieldExistence(Name, nameof(ApprovalFieldName), ApprovalFieldName).AddTo(result);

            //参照先は ApprovalFlowField であること (不在は上のチェックが指摘済み)
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            var target = ownModule?.Fields.FirstOrDefault(e => e.Name == ApprovalFieldName);
            if (target != null && target is not ApprovalFlowFieldDesign)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(ApprovalHistoryFieldDesign), Codes.NotApprovalFlowField),
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = nameof(ApprovalFieldName),
                    },
                    Message = string.Format(Properties.Resources.ApprovalCheck_NotApprovalFlowFieldFormat, ApprovalFieldName),
                });
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(ApprovalFieldName, x => ApprovalFieldName = x)
            .Build();
    }
}
