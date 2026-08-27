using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>ProgressField の表示形式。</summary>
    public enum ProgressDisplayType
    {
        /// <summary>横バー (プログレスバー)。</summary>
        [Designer(DisplayName = "$ProgressDisplayType_Bar")] Bar,
        /// <summary>半円メーター (ゲージ)。</summary>
        [Designer(DisplayName = "$ProgressDisplayType_Meter")] Meter,
    }
}
