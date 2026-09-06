using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Data
{
    /// <summary>
    /// TotpResetButtonField が Submit で書く値 (TOTP の 3 列)。列は書き込み専用なので読み込みでは常に空。
    /// 解除は <see cref="Cleared"/> の値 (秘密鍵 null / 確認済み 0 / 最終タイムステップ 0 = TotpLogin.ResetAsync と同じ) を書く。
    /// </summary>
    public class TotpResetButtonFieldData : FieldDataBase
    {
        public TotpResetButtonFieldData() : base(typeof(TotpResetButtonFieldData).FullName!) { }
        public string? TotpSecret { get; set; }
        public long? TotpConfirmed { get; set; }
        public long? TotpLastTimestep { get; set; }

        /// <summary>解除後の値。</summary>
        public static TotpResetButtonFieldData Cleared() => new() { TotpSecret = null, TotpConfirmed = 0, TotpLastTimestep = 0 };

        public override bool Equals(object? obj)
            => obj is TotpResetButtonFieldData o && TotpSecret == o.TotpSecret && TotpConfirmed == o.TotpConfirmed && TotpLastTimestep == o.TotpLastTimestep;
        public override int GetHashCode() => (TotpSecret, TotpConfirmed, TotpLastTimestep).GetHashCode();
        public TotpResetButtonFieldData Clone() => (TotpResetButtonFieldData)MemberwiseClone();
    }
}
