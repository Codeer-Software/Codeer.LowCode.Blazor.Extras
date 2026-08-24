using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    public class GmailTokenFieldData : FieldDataBase
    {
        public GmailTokenFieldData() : base(typeof(GmailTokenFieldData).FullName!) { }

        /// <summary>
        /// Gmail のリフレッシュトークン。クライアントから来るときは入力された平文
        /// (JSON {"refresh_token":"..."} かトークン文字列そのもの)、DB に入るときは暗号化済み文字列。
        /// 空 = 登録解除。
        /// </summary>
        public string? RefreshToken { get; set; }

        public override bool Equals(object? obj)
            => obj is GmailTokenFieldData r && RefreshToken == r.RefreshToken;

        public override int GetHashCode() => RefreshToken?.GetHashCode() ?? 0;
        public GmailTokenFieldData Clone() => (GmailTokenFieldData)MemberwiseClone();
    }
}
