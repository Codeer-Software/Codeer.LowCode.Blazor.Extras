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
    /// 詳細画面に置く。ChangeType が Delete の行でだけ押せる。
    /// 対象モジュールが論理削除なら Id を保ったまま戻す (明細も。リンクは切れない)。
    /// 物理削除ならスナップショットから新しいレコードを作る (Id は振り直し)。
    /// どちらも通常の保存経路なので対象モジュールの権限がそのまま効き、復活自体も履歴に残る。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "BackupRestore")]
    [Designer(DisplayName = "$EditHistoryRestoreButtonField")]
    public class EditHistoryRestoreButtonFieldDesign : FieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeContractFieldMissing = 1;

        public EditHistoryRestoreButtonFieldDesign() : base(typeof(EditHistoryRestoreButtonFieldDesign).FullName!) { }

        /// <summary>ボタンの文言。空なら既定 (「このレコードを復活」)。</summary>
        [Designer(Index = 2, DisplayName = "$EditHistoryRestoreButtonText")]
        public string Text { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(EditHistoryRestoreButtonFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new EditHistoryRestoreButtonField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            var ownModule = context.DesignData.Modules.Find(context.OwnerModule);
            if (ownModule != null && EditHistoryContracts.Contract(ownModule) == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(EditHistoryRestoreButtonFieldDesign), CodeContractFieldMissing),
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
