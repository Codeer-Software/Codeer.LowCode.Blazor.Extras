using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// ログイン中の自分の認証アプリ (TOTP) 登録を解除するボタン。表示中の行とは無関係で、どのモジュールにも置ける。
    /// 状態はサーバーに問い合わせる。エンドポイントは <see cref="TotpResetClient"/>。
    /// </summary>
    public class MyTotpResetButtonField(MyTotpResetButtonFieldDesign design) : FieldBase<MyTotpResetButtonFieldDesign>(design)
    {
        TotpStatus? _status;
        bool _loaded;
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
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        /// <summary>自分が認証アプリを登録済みか。未取得・機能無効なら null。</summary>
        public bool? IsRegistered => _status?.Enabled == true ? _status.Registered : null;

        [ScriptHide]
        public bool IsBusy => _isBusy;

        [ScriptHide]
        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? Properties.Resources.TotpResetButton_DefaultText : Design.Text;

        [ScriptHide]
        public string StatusText => IsRegistered == true ? Properties.Resources.TotpResetButton_Registered : Properties.Resources.TotpResetButton_NotRegistered;

        /// <summary>自分の状態をサーバーから取る (1 回)。</summary>
        [ScriptHide]
        public async Task EnsureStatusAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _loaded) return;
            _loaded = true;
            _status = await TotpResetClient.GetStatusAsync(Services);
            NotifyStateChanged();
        }

        /// <summary>確認の後、自分の認証アプリ登録を解除する。次回ログインで再登録になる。</summary>
        [ScriptName("Reset")]
        public async Task<bool> ResetAsync()
        {
            if (Services.AppInfoService.IsDesignMode || _isBusy) return false;
            _isBusy = true;
            NotifyStateChanged();
            try
            {
                var status = await TotpResetClient.ConfirmAndResetAsync(Services, Design.ConfirmMessage);
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
