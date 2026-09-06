using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>TotpSecretField の列の対応 (DbColumn 属性の DataMember 用)。ランタイムがこの値を送受信することは無い。</summary>
    public class TotpSecretFieldData : FieldDataBase
    {
        public TotpSecretFieldData() : base(typeof(TotpSecretFieldData).FullName!) { }
        public string? Secret { get; set; }
        public long? Confirmed { get; set; }
        public long? LastTimestep { get; set; }

        public override bool Equals(object? obj)
        {
            var r = obj as TotpSecretFieldData;
            if (r == null) return false;
            return Secret == r.Secret && Confirmed == r.Confirmed && LastTimestep == r.LastTimestep;
        }

        public override int GetHashCode() => (Secret, Confirmed, LastTimestep).GetHashCode();
        public TotpSecretFieldData Clone() => (TotpSecretFieldData)MemberwiseClone();
    }
}
