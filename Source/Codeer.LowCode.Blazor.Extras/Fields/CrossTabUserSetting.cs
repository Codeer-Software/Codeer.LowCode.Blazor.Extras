using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 利用者がカスタマイズしたクロス集計の設定。ブラウザ (localStorage) に JSON で保存する。キーは「モジュール名.フィールド名」(ListField のカラムカスタマイズと同じ)。
    /// </summary>
    public class CrossTabUserSetting
    {
        public CrossTabSetting Setting { get; set; } = new();
        public CrossTabValueDisplay ValueDisplay { get; set; }
    }
}
