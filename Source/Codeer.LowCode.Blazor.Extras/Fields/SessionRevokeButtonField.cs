using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 表示中の行 (ログインユーザーモジュールの 1 行) のユーザーを全端末からログアウトさせるボタン (管理者用)。
    /// ログアウトは「セッションの印の列を NULL にする」データを持って通常の Submit を通す (SubmitButtonField と同じ経路)。
    /// 権限は CLB の権限モデル (UserWrite / DataWrite 条件) がそのまま効くので、サーバーに専用の入口は無い。
    /// </summary>
    public class SessionRevokeButtonField(SessionRevokeButtonFieldDesign design) : FieldBase<SessionRevokeButtonFieldDesign>(design)
    {
        bool _revokeRequested;
        bool _isBusy;

        /// <summary>ログアウトが要求され、まだ保存されていない間だけ true。</summary>
        [ScriptHide]
        public override bool IsModified => _revokeRequested;

        [ScriptHide]
        public override FieldDataBase? GetData() => _revokeRequested ? SessionRevokeButtonFieldData.Cleared() : null;

        /// <summary>ログアウトが要求されているときだけ印を NULL にする値を送る (それ以外は何も書かない)。</summary>
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new() { FieldData = _revokeRequested ? SessionRevokeButtonFieldData.Cleared() : null };

        //列は書き込み専用なので読み込みで値は来ない。行が変わったら要求は捨てる
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase)
        {
            _revokeRequested = false;
            return Task.CompletedTask;
        }

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _revokeRequested = false;
            return Task.CompletedTask;
        }

        [ScriptHide]
        public bool IsBusy => _isBusy;

        /// <summary>新規行 (まだユーザーが無い) では出さない。</summary>
        [ScriptHide]
        public bool CanRevoke => Module != null && !Module.IsNewData && IsEnabled && !IsViewOnly;

        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? Properties.Resources.SessionRevokeButton_DefaultText : Design.Text;

        /// <summary>
        /// 確認の後、表示中の行のユーザーを全端末からログアウトさせる (印を NULL にしてモジュールを保存)。次のログインで新しい印が採番される。
        /// 保存はモジュール全体の Submit なので、編集中の他のフィールドも一緒に保存される。成功なら true。
        /// </summary>
        [ScriptName("Revoke")]
        public async Task<bool> RevokeAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isBusy || !CanRevoke) return false;
            if (!await Module!.ValidateInput())
            {
                await Services.UIService.NotifyError(Properties.Resources.InvalidInput);
                return false;
            }

            var message = string.IsNullOrEmpty(Design.ConfirmMessage) ? Properties.Resources.SessionRevokeButton_Confirm : Design.ConfirmMessage;
            var answer = await Services.UIService.ShowMessageBox(string.Empty, message,
                [new DialogButton("btn btn-outline-danger", Properties.Resources.SessionRevokeButton_Action),
                 new DialogButton("btn btn-outline-secondary", Properties.Resources.Cancel)]);
            if (answer != Properties.Resources.SessionRevokeButton_Action) return false;

            _isBusy = true;
            _revokeRequested = true;
            NotifyStateChanged();
            try
            {
                var result = await Module!.SubmitAsync();
                if (result != true)
                {
                    _revokeRequested = false;
                    await Services.UIService.NotifyError(Properties.Resources.SessionRevokeButton_Failed);
                    return false;
                }
                //保存後はデータの再読み込みで InitializeDataAsync が呼ばれ要求は消える
                _revokeRequested = false;
                await Services.UIService.NotifySuccess(Properties.Resources.SessionRevokeButton_Done);
                return true;
            }
            finally
            {
                _isBusy = false;
                NotifyStateChanged();
            }
        }
    }
}
