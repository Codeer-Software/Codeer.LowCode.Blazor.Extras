using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>LoginAccountContractField のログイン用列の対応 (DbColumn 属性の DataMember 用)。ランタイムがこの値を送受信することは無い。</summary>
    public class LoginAccountContractFieldData : FieldDataBase
    {
        public LoginAccountContractFieldData() : base(typeof(LoginAccountContractFieldData).FullName!) { }
        public string? PasswordHash { get; set; }
        public string? PasswordSalt { get; set; }
        public string? TotpSecret { get; set; }
        public long? TotpConfirmed { get; set; }
        public long? TotpLastTimestep { get; set; }

        public override bool Equals(object? obj)
        {
            var r = obj as LoginAccountContractFieldData;
            if (r == null) return false;
            return PasswordHash == r.PasswordHash && PasswordSalt == r.PasswordSalt && TotpSecret == r.TotpSecret && TotpConfirmed == r.TotpConfirmed && TotpLastTimestep == r.TotpLastTimestep;
        }

        public override int GetHashCode() => (PasswordHash, PasswordSalt, TotpSecret, TotpConfirmed, TotpLastTimestep).GetHashCode();
        public LoginAccountContractFieldData Clone() => (LoginAccountContractFieldData)MemberwiseClone();
    }
}
