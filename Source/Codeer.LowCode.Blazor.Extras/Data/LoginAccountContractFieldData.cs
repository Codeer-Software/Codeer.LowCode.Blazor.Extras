using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// LoginAccountContractField がパスワードのハッシュ / ソルト列に書く値。
    /// クライアントは送受信しない。保存時にサーバー (PasswordHashHelper.ApplyPasswordHash) が契約の PasswordField の平文から作って
    /// ModuleData に差し込み、本体が契約の書き込み専用列 (DbColumnPasswordHash / DbColumnPasswordSalt) へ書く。
    /// 認証アプリ (TOTP) の列はここに含めない (含めるとパスワード保存のたびに NULL で上書きされる。TOTP の列はサーバーのログイン処理が直接読み書きする)。
    /// </summary>
    public class LoginAccountContractFieldData : FieldDataBase
    {
        public LoginAccountContractFieldData() : base(typeof(LoginAccountContractFieldData).FullName!) { }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }

        public override bool Equals(object? obj)
            => obj is LoginAccountContractFieldData r && PasswordHash == r.PasswordHash && PasswordSalt == r.PasswordSalt;

        public override int GetHashCode() => (PasswordHash, PasswordSalt).GetHashCode();
        public LoginAccountContractFieldData Clone() => (LoginAccountContractFieldData)MemberwiseClone();
    }
}
