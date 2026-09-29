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
    /// 削除されたレコードを履歴から復活させるボタン。履歴モジュール (EditHistoryContractField を置いたモジュール) の
    /// 詳細画面に置く。そのレコードの最新の版が削除 (ChangeType が Delete) で、対象モジュールで削除できる人にだけ出る。
    /// クライアントは履歴行の Id だけを送り、サーバー (EditHistoryRecorder) が版のスナップショットからレコード全体を戻す。
    /// 対象モジュールが論理削除なら Id を保ったまま戻す (明細も。リンクは切れない)。
    /// 物理削除なら作り直す (手入力 Id は元の Id、自動採番は新しい Id で、旧 Id の版を新しい Id に付け替える)。
    /// 権限は削除と同じ。復活は Restore の版として履歴に残る。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "BackupRestore")]
    [Designer(DisplayName = "$EditHistoryUndeleteButtonField")]
    public class EditHistoryUndeleteButtonFieldDesign : FieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeContractFieldMissing = 1;

        public EditHistoryUndeleteButtonFieldDesign() : base(typeof(EditHistoryUndeleteButtonFieldDesign).FullName!) { }

        /// <summary>ボタンの文言。空なら既定 (「このレコードを復活」)。</summary>
        [Designer(Index = 2, DisplayName = "$EditHistoryUndeleteButtonText")]
        public string Text { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(EditHistoryUndeleteButtonFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new EditHistoryUndeleteButtonField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && EditHistoryContracts.Contract(ownModule) == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryUndeleteButtonFieldDesign), CodeContractFieldMissing),
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
