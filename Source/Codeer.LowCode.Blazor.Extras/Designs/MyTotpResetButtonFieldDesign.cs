using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ログイン中の自分の認証アプリ (TOTP) 登録を解除するボタン。どのモジュールにも置ける (設定画面・マイページ等)。
    /// 対象は常にログイン中のユーザーで、表示中の行とは無関係。登録済みなら「認証アプリ: 登録済み」と解除ボタン、未登録なら状態だけ。
    /// 認証アプリの二要素認証を使っていないアプリでは何も出さない。他人の登録を解除するのは TotpResetButtonField (ユーザーモジュールの詳細画面に置く)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CellphoneKey")]
    [Designer(DisplayName = "$MyTotpResetButtonField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class MyTotpResetButtonFieldDesign() : FieldDesignBase(typeof(MyTotpResetButtonFieldDesign).FullName!)
    {
        /// <summary>ボタンの文字。空なら既定 ("認証アプリを解除")。</summary>
        [Designer(Index = 2, DisplayName = "$TotpResetButtonText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>解除前の確認メッセージ。空なら既定。</summary>
        [Designer(Index = 3, DisplayName = "$TotpResetButtonConfirmMessage")]
        public string ConfirmMessage { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(MyTotpResetButtonFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new MyTotpResetButtonField(this);
        public override FieldDataBase? CreateData() => null;
    }
}
