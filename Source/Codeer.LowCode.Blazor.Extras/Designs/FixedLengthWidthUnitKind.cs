using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 固定長形式の列幅の単位 (CsvFileFormatFieldDesign.FixedLengthWidthUnit)。
    /// 固定長形式 (Delimiter = None) のときだけ使われる。
    /// 既定は Byte (全銀協フォーマット等、Shift_JIS のバイト桁で定義されるレガシー固定長が典型。全角 = 2 バイト)。
    /// </summary>
    public enum FixedLengthWidthUnitKind
    {
        [Designer(DisplayName = "$FixedLengthWidthUnitKind_Byte")] Byte,
        [Designer(DisplayName = "$FixedLengthWidthUnitKind_Char")] Char,
    }
}
