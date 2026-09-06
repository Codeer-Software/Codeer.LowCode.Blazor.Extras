using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 外部 IdP 1 つ分。プロバイダごとの実装 (Entra ID / Google / AWS Cognito / 汎用 OIDC) があり、
    /// それぞれ自分のプロバイダ設定だけを受け取る。IdP ごとの癖 (エンドポイント・クレームの取り方・検証・ログアウト) は
    /// このインターフェースが吸収する (製品側にプロバイダ共通の設定型は無い)。
    /// 独自の IdP はこれを実装 (多くは <see cref="OidcLoginProvider"/> を継承) して、テンプレートの対応表 (ExternalLoginTable) に足す。
    /// </summary>
    public interface IExternalLoginProvider
    {
        /// <summary>プロバイダ名。URL (/api/account/login/{Name}、コールバック /signin-{name})・Cookie の idp クレーム・ログイン画面のボタンの識別に使う。英数字と - _ のみ。</summary>
        string Name { get; }

        /// <summary>ログイン画面のボタンに出す名前。</summary>
        string DisplayName { get; }

        /// <summary>
        /// OpenID Connect ハンドラの設定。呼ばれる時点で共通の既定 (Cookie へのサインイン・コールバックパス・code + PKCE・
        /// クレーム名は id_token のまま・トークン保持) は入っているので、Authority / クライアント / スコープ / 検証ルールを入れる。
        /// </summary>
        void Configure(OpenIdConnectOptions options);

        /// <summary>
        /// IdP が返したクレームを検証して本人情報にする。拒否するときは null を返し、理由を <see cref="ExternalLoginError"/> の値で error に入れる
        /// (ログイン画面に ?error= で戻る)。
        /// </summary>
        ExternalLoginIdentity? CreateIdentity(ClaimsPrincipal principal, out string error);

        /// <summary>
        /// ログアウト時に IdP 側のセッションも終わらせる必要があるか。true ならアプリはブラウザ遷移で <see cref="SignOutAsync"/> を呼ぶ
        /// (fetch の POST からは IdP へ遷移できないため二段構え)。false なら Cookie を破棄するだけでよい。
        /// </summary>
        Task<bool> HasIdpSignOutAsync(OpenIdConnectOptions options);

        /// <summary>Cookie を破棄し、IdP のセッションも終わらせて redirectUri (アプリ内パス) へ戻る。<see cref="HasIdpSignOutAsync"/> が true のときだけ呼ばれる。</summary>
        Task<IActionResult> SignOutAsync(ControllerBase controller, string scheme, string redirectUri);
    }

    /// <summary>ログイン画面へ差し戻すときの理由 (login.html?error=...)。</summary>
    public static class ExternalLoginError
    {
        public const string RemoteFailure = "remote_failure";
        public const string InvalidClaims = "invalid_claims";
        public const string GuestNotAllowed = "guest_not_allowed";
        public const string DomainNotAllowed = "domain_not_allowed";
        public const string UserNotRegistered = "user_not_registered";
    }
}
