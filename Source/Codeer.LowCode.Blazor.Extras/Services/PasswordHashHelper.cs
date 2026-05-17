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
        /// For every <see cref="PasswordHashFieldDesign"/> on the module, if the referenced
        /// <c>PasswordField</c> has a value, replace the hash field data with a freshly computed hash+salt.
        /// </summary>
        public static void ApplyPasswordHash(ModuleDesign moduleDesign, ModuleData data)
        {
            foreach (var hashFieldDesign in moduleDesign.Fields.OfType<PasswordHashFieldDesign>())
            {
                if (string.IsNullOrEmpty(hashFieldDesign.PasswordFieldName)) continue;
                if (!data.Fields.TryGetValue(hashFieldDesign.PasswordFieldName, out var passwordFieldData)) continue;
                var password = (passwordFieldData as PasswordFieldData)?.Value;
                if (string.IsNullOrEmpty(password)) continue;

                data.Fields[hashFieldDesign.Name] = CreateHash(password);
            }
        }
    }
}
