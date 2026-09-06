using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 認証アプリ (TOTP) の登録を解除するボタン。表示中の行 (ログインユーザーモジュールの 1 行) のユーザーが対象。
    /// 自分の行なら本人解除、他人の行なら「その行を編集できる人」だけ (サーバーが判定)。
    /// 状態はサーバーに問い合わせる (TOTP の列は書き込み専用でクライアントには来ない)。エンドポイントは <see cref="TotpResetClient"/>。
    /// </summary>
    public class TotpResetButtonField(TotpResetButtonFieldDesign design) : FieldBase<TotpResetButtonFieldDesign>(design)
    {
        TotpStatus? _status;
        string? _loadedForId;
        bool _isBusy;

        //値を持たないフィールド
        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            //行が変わったら状態を取り直す
            _status = null;
            _loadedForId = null;
            return Task.CompletedTask;
        }

        /// <summary>表示中の行のユーザーが認証アプリを登録済みか。未取得・新規行・機能無効なら null。</summary>
        public bool? IsRegistered => _status?.Enabled == true ? _status.Registered : null;

        [ScriptHide]
        public bool IsBusy => _isBusy;

        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? Properties.Resources.TotpResetButton_DefaultText : Design.Text;

        [ScriptHide]
        public string StatusText => IsRegistered == true ? Properties.Resources.TotpResetButton_Registered : Properties.Resources.TotpResetButton_NotRegistered;

        string TargetUserId => Module == null || Module.IsNewData ? string.Empty : Module.GetIdText();

        /// <summary>表示中の行の状態をサーバーから取る (行ごとに 1 回)。</summary>
        [ScriptHide]
        public async Task EnsureStatusAsync()
        {
            if (Services.AppInfoService.IsDesignMode) return;
            var id = TargetUserId;
            if (string.IsNullOrEmpty(id) || id == _loadedForId) return;
            _loadedForId = id;
            _status = await TotpResetClient.GetStatusAsync(Services, id);
            NotifyStateChanged();
        }

        /// <summary>確認の後、表示中の行のユーザーの認証アプリ登録を解除する。次回ログインで再登録になる。</summary>
        [ScriptName("Reset")]
        public async Task<bool> ResetAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isBusy) return false;
            var id = TargetUserId;
            if (string.IsNullOrEmpty(id)) return false;

            _isBusy = true;
            NotifyStateChanged();
            try
            {
                var status = await TotpResetClient.ConfirmAndResetAsync(Services, id, Design.ConfirmMessage);
                if (status == null) return false;
                _status = status;
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
