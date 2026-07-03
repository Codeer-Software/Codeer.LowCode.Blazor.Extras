namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// QRコードの誤り訂正レベル (QRCoder の ECCLevel に対応)。
    /// </summary>
    public enum QrCodeEccLevel
    {
        /// <summary>約7%まで復元可能。</summary>
        L,

        /// <summary>約15%まで復元可能 (既定)。</summary>
        M,

        /// <summary>約25%まで復元可能。</summary>
        Q,

        /// <summary>約30%まで復元可能。</summary>
        H,
    }
}
