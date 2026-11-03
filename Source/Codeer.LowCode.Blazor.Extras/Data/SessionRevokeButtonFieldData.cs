using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// SessionRevokeButtonField が Submit で書く値 (セッションの印の列)。列は書き込み専用なので読み込みでは常に空。
    /// ログアウトは <see cref="Cleared"/> の値 (印 null) を書く。
    /// </summary>
    public class SessionRevokeButtonFieldData : FieldDataBase
    {
        public SessionRevokeButtonFieldData() : base(typeof(SessionRevokeButtonFieldData).FullName!) { }
        public string? SessionStamp { get; set; }

        /// <summary>ログアウト後の値。</summary>
        public static SessionRevokeButtonFieldData Cleared() => new() { SessionStamp = null };

        public override bool Equals(object? obj) => obj is SessionRevokeButtonFieldData o && SessionStamp == o.SessionStamp;
        public override int GetHashCode() => SessionStamp?.GetHashCode() ?? 0;
        public SessionRevokeButtonFieldData Clone() => (SessionRevokeButtonFieldData)MemberwiseClone();
    }
}
