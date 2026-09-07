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
    /// Call <see cref="ApplyPasswordHash"/> from your <see cref="ModuleDataIO"/> override
    /// (typically <c>CustomizedModuleDataIO.AddAsync</c> / <c>UpdateAsync</c>) to hash
    /// plain passwords before persistence.
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
            return Convert.ToBase64String(hashBytes) == hash;
        }

        /// <summary>
        /// 保存前にパスワードの平文をハッシュ + ソルトへ置き換える。
        /// <list type="bullet">
        /// <item><see cref="LoginAccountContractFieldDesign"/> の <c>PasswordField</c> が指す PasswordField に値があれば、契約のデータ (ハッシュ / ソルト列) を差し込む。</item>
        /// <item><see cref="PasswordHashFieldDesign"/> は参照先の PasswordField に値があればハッシュ + ソルトを差し込む (契約が同じ列を書くモジュールでは飛ばす = 二重書き防止)。</item>
        /// </list>
        /// </summary>
        public static void ApplyPasswordHash(ModuleDesign moduleDesign, ModuleData data)
        {
            var contract = moduleDesign.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault(e => e.WritesPassword);
            if (contract != null)
            {
                var password = PlainPassword(data, contract.PasswordField);
                if (password != null)
                {
                    var hashed = CreateHash(password);
                    data.Fields[contract.Name] = new LoginAccountContractFieldData { PasswordHash = hashed.Hash, PasswordSalt = hashed.Salt };
                }
            }

            foreach (var hashFieldDesign in moduleDesign.Fields.OfType<PasswordHashFieldDesign>())
            {
                if (contract != null) continue;   //契約がハッシュを書くモジュールでは PasswordHashField は使わない (デザインチェックも併用をエラーにする)
                var password = PlainPassword(data, hashFieldDesign.PasswordFieldName);
                if (password == null) continue;
                data.Fields[hashFieldDesign.Name] = CreateHash(password);
            }
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
