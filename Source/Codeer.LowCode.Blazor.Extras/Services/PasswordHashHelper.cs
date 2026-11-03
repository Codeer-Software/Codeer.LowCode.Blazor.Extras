using System.Security.Cryptography;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// Server-side helper for <see cref="PasswordHashFieldDesign"/>.
    /// PBKDF2-HMAC-SHA256, 100,000 iterations, 32-byte salt, 32-byte hash, base64-encoded.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 反復回数は 10 万回で固定し、値に回数を持たせない (仕様。2026-11-04 決定)。回数を上げると版を戻したとき・自前の照合で既存のハッシュが通らなくなる。
    /// セキュリティ要件の厳しいアプリはパスワードでなく外部 IdP (Entra ID 等) を使う前提。
    /// </para>
    /// <para>
    /// <c>PasswordHashingModuleDataIO</c> (Codeer.LowCode.Blazor.Extras.Server) calls <see cref="ApplyPasswordHash"/>
    /// from its <see cref="ModuleDataIO"/> overrides (AddAsync / UpdateAsync / BulkAddAsync); the app template inherits it.
    /// Call it yourself only when you derive from <see cref="ModuleDataIO"/> directly.
    /// </remarks>
    public static class PasswordHashHelper
    {
        public static PasswordHashFieldData CreateHash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(32);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100000, HashAlgorithmName.SHA256, 32);
            return new PasswordHashFieldData
            {
                Hash = Convert.ToBase64String(hash),
                Salt = Convert.ToBase64String(salt)
            };
        }

        public static bool VerifyHash(string password, string hash, string salt)
        {
            var saltBytes = Convert.FromBase64String(salt);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 100000, HashAlgorithmName.SHA256, 32);
            byte[] expected;
            try
            {
                expected = Convert.FromBase64String(hash);
            }
            catch (FormatException)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(hashBytes, expected);
        }

        /// <summary>
        /// 保存前にパスワードの平文をハッシュ + ソルトへ置き換える。
        /// <list type="bullet">
        /// <item><see cref="LoginAccountContractFieldDesign"/> の <c>PasswordField</c> が指す PasswordField に値があれば、契約のデータ (ハッシュ / ソルト列。
        /// 契約に <c>DbColumnSessionStamp</c> があれば新しいセッションの印も) を差し込む。
        /// 印を書くときは送信に載った <see cref="SessionRevokeButtonFieldDesign"/> のデータを外す (同じ列を二重に書かない。新しい印でも全端末は失効する)。</item>
        /// <item><see cref="PasswordHashFieldDesign"/> は参照先の PasswordField に値があればハッシュ + ソルトを差し込む (契約が同じ列を書くモジュールでは飛ばす = 二重書き防止)。</item>
        /// </list>
        /// ハッシュ / ソルト / 印 / TOTP の列の値はサーバーだけが作る: 送られてきたデータに契約や PasswordHashField のデータが載っていても捨てる
        /// (SessionRevokeButtonField のデータは印 NULL に、TotpResetButtonField のデータは未登録の値に揃える = ボタンが送る値そのもの)
        /// (平文が無ければ列は書かない = クライアントがハッシュ・印・TOTP の鍵を直接書き込むことはできない)。
        /// 戻り値はパスワードを書き換えたか (どちらの経路でも)。
        /// </summary>
        public static bool ApplyPasswordHash(ModuleDesign moduleDesign, ModuleData data)
        {
            var written = false;
            foreach (var e in moduleDesign.Fields.OfType<LoginAccountContractFieldDesign>()) data.Fields.Remove(e.Name);
            foreach (var e in moduleDesign.Fields.OfType<PasswordHashFieldDesign>()) data.Fields.Remove(e.Name);
            foreach (var e in moduleDesign.Fields.OfType<SessionRevokeButtonFieldDesign>())
            {
                if (data.Fields.ContainsKey(e.Name)) data.Fields[e.Name] = SessionRevokeButtonFieldData.Cleared();
            }
            foreach (var e in moduleDesign.Fields.OfType<TotpResetButtonFieldDesign>())
            {
                if (data.Fields.ContainsKey(e.Name)) data.Fields[e.Name] = TotpResetButtonFieldData.Cleared();
            }

            var contract = moduleDesign.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault(e => e.WritesPassword);
            if (contract != null)
            {
                var password = PlainPassword(data, contract.PasswordField);
                if (password != null)
                {
                    var hashed = CreateHash(password);
                    data.Fields[contract.Name] = new LoginAccountContractFieldData
                    {
                        PasswordHash = hashed.Hash, PasswordSalt = hashed.Salt,
                        SessionStamp = contract.HasSessionStamp ? SessionStamp.Create() : null,
                    };
                    if (contract.HasSessionStamp)
                    {
                        foreach (var e in moduleDesign.Fields.OfType<SessionRevokeButtonFieldDesign>()) data.Fields.Remove(e.Name);
                    }
                    written = true;
                }
            }

            foreach (var hashFieldDesign in moduleDesign.Fields.OfType<PasswordHashFieldDesign>())
            {
                if (contract != null) continue;   //契約がハッシュを書くモジュールでは PasswordHashField は使わない (デザインチェックも併用をエラーにする)
                var password = PlainPassword(data, hashFieldDesign.PasswordFieldName);
                if (password == null) continue;
                data.Fields[hashFieldDesign.Name] = CreateHash(password);
                written = true;
            }
            return written;
        }

        //送られてきたデータにある PasswordField の平文 (無ければ null = 今回はパスワードを変えない)
        static string? PlainPassword(ModuleData data, string passwordFieldName)
        {
            if (string.IsNullOrEmpty(passwordFieldName)) return null;
            if (!data.Fields.TryGetValue(passwordFieldName, out var passwordFieldData)) return null;
            var password = (passwordFieldData as PasswordFieldData)?.Value;
            return string.IsNullOrEmpty(password) ? null : password;
        }
    }
}
