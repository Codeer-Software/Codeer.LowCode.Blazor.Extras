using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// LoginAccountContractField がパスワードのハッシュ / ソルト列とセッションの印の列に書く値。
    /// クライアントは送受信しない。保存時にサーバー (PasswordHashHelper.ApplyPasswordHash) が契約の PasswordField の平文から作って
    /// ModuleData に差し込み、本体が契約の書き込み専用列 (DbColumnPasswordHash / DbColumnPasswordSalt / DbColumnSessionStamp) へ書く。
    /// 3 列は 1 つの UPDATE で一緒に書かれる (パスワードが変わるたびに印も新しくなる = それまでの Cookie が全部切れる)。
    /// 認証アプリ (TOTP) の列はここに含めない (含めるとパスワード保存のたびに NULL で上書きされる。TOTP の列はサーバーのログイン処理が直接読み書きする)。
    /// </summary>
    public class LoginAccountContractFieldData : FieldDataBase
    {
        public LoginAccountContractFieldData() : base(typeof(LoginAccountContractFieldData).FullName!) { }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        /// <summary>セッションの印 (契約に DbColumnSessionStamp があるときだけ。無ければ null)。</summary>
        public string? SessionStamp { get; set; }

        public override bool Equals(object? obj)
            => obj is LoginAccountContractFieldData r && PasswordHash == r.PasswordHash && PasswordSalt == r.PasswordSalt && SessionStamp == r.SessionStamp;

        public override int GetHashCode() => (PasswordHash, PasswordSalt, SessionStamp).GetHashCode();
        public LoginAccountContractFieldData Clone() => (LoginAccountContractFieldData)MemberwiseClone();
    }
}
