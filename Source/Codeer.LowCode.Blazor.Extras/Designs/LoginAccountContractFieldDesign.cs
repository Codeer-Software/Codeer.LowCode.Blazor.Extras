using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ログインアカウント契約。ログインユーザーモジュール (AppSettings.CurrentUserModuleDesignName) に 1 つ置き、
    /// 「このモジュールの行がログインアカウントとしてどう振る舞うか」を役割 → 自モジュールのフィールド名で宣言する。
    /// サーバー側のログイン (Codeer.LowCode.Blazor.Extras.Server.Auth.LoginAccountStore) はこの契約でユーザー行を探す:
    /// ID/パスワードの照合、外部 IdP (Entra 等) で確認した本人のユーザー行への解決、停止ユーザーの拒否、初回の管理者作成。
    /// パスワードを使わない構成 (外部 IdP 専用) でもログイン ID の宣言は要るので、PasswordHashField ではなくこの契約が持つ。
    /// ハッシュ / ソルト (PasswordHashField) と TOTP の列 (TotpSecretField) は書き込み専用列の宣言を兼ねるのでフィールドのまま (型で見つける)。
    /// UI もデータも持たない (DB 列不要)。
    /// </summary>
    [Designer(DisplayName = "$LoginAccountContractField")]
    [ToolboxIcon(PackIconMaterialKind = "AccountKeyOutline")]
    public class LoginAccountContractFieldDesign : ContractFieldDesignBase
    {
        public LoginAccountContractFieldDesign() : base(typeof(LoginAccountContractFieldDesign).FullName!) { }

        /// <summary>ログイン ID を持つ自モジュールのフィールド (ユーザー名・メール等。DB 列を持つ値フィールド)。ID/パスワードのログインで入力する値と比較する。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractLoginName")]
        public string LoginName { get; set; } = nameof(LoginName);

        /// <summary>
        /// 外部 IdP が確認した本人 (Entra = UPN、Google / Cognito = メール) と突き合わせるフィールド。
        /// 空なら <see cref="LoginName"/> と同じ列で突き合わせる (社内のログイン ID とメールが別ならここにメールのフィールドを指定する)。
        /// </summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractExternalLoginName")]
        public string ExternalLoginName { get; set; } = string.Empty;

        /// <summary>有効フラグ (Boolean 等)。偽ならログインを拒否する (退職・停止)。空なら判定しない。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractIsActive")]
        public string IsActive { get; set; } = string.Empty;

        /// <summary>表示名。Cookie の Name クレーム (画面のユーザー表示) と二要素認証の QR のアカウント名に使う。空なら <see cref="LoginName"/>。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractDisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// メールのワンタイムコードによる二要素認証の送信先 (メールアドレスのフィールド)。設定するとパスワード成功後にコードを送って入力を求める。
        /// 空なら無効。同じモジュールに TotpSecretField があればそちら (認証アプリ) が優先される。
        /// </summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractTwoFactorEmail")]
        public string TwoFactorEmail { get; set; } = string.Empty;

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(LoginName) };
    }
}
