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
    /// 表示中の行 (ログインユーザーモジュールの 1 行) のユーザーの認証アプリ (TOTP) 登録を解除するボタン。
    /// 解除は「TOTP の 3 列を空にする」データを持って通常の Submit を通す (SubmitButtonField と同じ経路)。
    /// 権限は CLB の権限モデル (UserWrite / DataWrite 条件) がそのまま効くので、サーバーに専用の入口は無い。
    /// </summary>
    public class TotpResetButtonField(TotpResetButtonFieldDesign design) : FieldBase<TotpResetButtonFieldDesign>(design)
    {
        bool _resetRequested;
        bool _isBusy;

        /// <summary>解除が要求され、まだ保存されていない間だけ true。</summary>
        [ScriptHide]
        public override bool IsModified => _resetRequested;

        [ScriptHide]
        public override FieldDataBase? GetData() => _resetRequested ? TotpResetButtonFieldData.Cleared() : null;

        /// <summary>解除が要求されているときだけ 3 列を空にする値を送る (それ以外は何も書かない)。</summary>
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new() { FieldData = _resetRequested ? TotpResetButtonFieldData.Cleared() : null };

        //列は書き込み専用なので読み込みで値は来ない。行が変わったら要求は捨てる
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase)
        {
            _resetRequested = false;
            return Task.CompletedTask;
        }

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _resetRequested = false;
            return Task.CompletedTask;
        }

        [ScriptHide]
        public bool IsBusy => _isBusy;

        /// <summary>新規行 (まだユーザーが無い) では出さない。</summary>
        [ScriptHide]
        public bool CanReset => Module != null && !Module.IsNewData && IsEnabled;

        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? Properties.Resources.TotpResetButton_DefaultText : Design.Text;

        /// <summary>
        /// 確認の後、表示中の行のユーザーの認証アプリ登録を解除する (3 列を空にしてモジュールを保存)。次回ログインで再登録になる。
        /// 保存はモジュール全体の Submit なので、編集中の他のフィールドも一緒に保存される。成功なら true。
        /// </summary>
        [ScriptName("Reset")]
        public async Task<bool> ResetAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isBusy || !CanReset) return false;

            var message = string.IsNullOrEmpty(Design.ConfirmMessage) ? Properties.Resources.TotpResetButton_Confirm : Design.ConfirmMessage;
            var answer = await Services.UIService.ShowMessageBox(string.Empty, message,
                [new DialogButton("btn btn-outline-danger", Properties.Resources.TotpResetButton_Action),
                 new DialogButton("btn btn-outline-secondary", Properties.Resources.Cancel)]);
            if (answer != Properties.Resources.TotpResetButton_Action) return false;

            _isBusy = true;
            _resetRequested = true;
            NotifyStateChanged();
            try
            {
                var result = await Module!.SubmitAsync();
                if (result != true)
                {
                    _resetRequested = false;
                    await Services.UIService.NotifyError(Properties.Resources.TotpResetButton_Failed);
                    return false;
                }
                //保存後はデータの再読み込みで InitializeDataAsync が呼ばれ要求は消える
                _resetRequested = false;
                await Services.UIService.NotifySuccess(Properties.Resources.TotpResetButton_Done);
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
