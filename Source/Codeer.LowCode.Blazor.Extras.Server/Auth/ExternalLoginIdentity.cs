using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>外部 IdP で本人確認できた結果。アプリはこれをユーザーテーブルの 1 行に解決する (<see cref="IExternalLoginUserResolver"/>)。</summary>
    public class ExternalLoginIdentity
    {
        /// <summary>プロバイダ名 (<see cref="IExternalLoginProvider.Name"/>)。</summary>
        public string Provider { get; init; } = string.Empty;

        /// <summary>IdP 内で不変のユーザー識別子 (sub)。メールが変わっても変わらないので、紐付けテーブルを持つならこれをキーにする。</summary>
        public string Subject { get; init; } = string.Empty;

        /// <summary>ユーザー名。Entra は UPN、Google / Cognito はメール (各プロバイダの設定 LoginNameClaim で変更できるものもある)。</summary>
        public string LoginName { get; init; } = string.Empty;

        public string? Email { get; init; }

        public string? DisplayName { get; init; }

        /// <summary>トークンの発行者。Entra のマルチテナント構成ではテナントごとに異なる。</summary>
        public string? Issuer { get; init; }

        /// <summary>IdP が返した全クレーム (id_token のクレーム名のまま)。</summary>
        public ClaimsPrincipal Claims { get; init; } = new();
    }

    /// <summary>解決できたアプリのユーザー。Cookie にはこの 2 つ (と経路のプロバイダ名) だけを積む。</summary>
    public record ExternalLoginUser(string UserId, string UserName);

    /// <summary>
    /// 外部 IdP で確認した本人をアプリのユーザーに解決する。実装はアプリ側 (ユーザーテーブルの形とプロビジョニング方針はアプリごとに違うため)。
    /// null を返すとサインインせずログイン画面に error=user_not_registered で差し戻す。
    /// </summary>
    public interface IExternalLoginUserResolver
    {
        Task<ExternalLoginUser?> ResolveAsync(ExternalLoginIdentity identity);
    }
}
