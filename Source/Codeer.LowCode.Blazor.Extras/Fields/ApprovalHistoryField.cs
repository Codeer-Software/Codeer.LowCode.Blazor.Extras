using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 承認履歴表示フィールドのランタイム。同一モジュール上の ApprovalFlowField を参照して
    /// その履歴を表示するだけの表示部品 (データ・送信なし)。
    /// </summary>
    public class ApprovalHistoryField(ApprovalHistoryFieldDesign design) : FieldBase<ApprovalHistoryFieldDesign>(design)
    {
        ApprovalFlowField? _target;

        /// <summary>表示元の承認フローフィールド。</summary>
        [ScriptHide]
        public ApprovalFlowField? Target => _target;

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            //参照先のアクション成功・再読込に合わせて自分も再描画する
            if (_target != null) _target.ViewStateChanged -= NotifyStateChanged;
            _target = Module?.GetField<ApprovalFlowField>(Design.ApprovalFieldName);
            if (_target != null) _target.ViewStateChanged += NotifyStateChanged;
            await Task.CompletedTask;
        }

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase)
            => await InitializeDataAsync(fieldDataBase);
    }
}
