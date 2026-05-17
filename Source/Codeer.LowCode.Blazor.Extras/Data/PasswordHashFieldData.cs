using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class PasswordHashFieldData : FieldDataBase
    {
        public PasswordHashFieldData() : base(typeof(PasswordHashFieldData).FullName!) { }
        public string? Hash { get; set; }
        public string? Salt { get; set; }

        public override bool Equals(object? obj)
        {
            var r = obj as PasswordHashFieldData;
            if (r == null) return false;
            return Hash == r.Hash && Salt == r.Salt;
        }

        public override int GetHashCode() => (Hash, Salt).GetHashCode();
        public PasswordHashFieldData Clone() => (PasswordHashFieldData)MemberwiseClone();
    }
}
