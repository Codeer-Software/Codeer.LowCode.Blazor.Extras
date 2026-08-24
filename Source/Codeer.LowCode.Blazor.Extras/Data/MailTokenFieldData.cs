using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class MailTokenFieldData : FieldDataBase
    {
        public MailTokenFieldData() : base(typeof(MailTokenFieldData).FullName!) { }

        /// <summary>リフレッシュトークン (JSON {"refresh_token":"..."} かトークン文字列そのもの)。</summary>
        public string? Token { get; set; }

        public override bool Equals(object? obj)
            => obj is MailTokenFieldData r && Token == r.Token;

        public override int GetHashCode() => Token?.GetHashCode() ?? 0;
        public MailTokenFieldData Clone() => (MailTokenFieldData)MemberwiseClone();
    }
}
