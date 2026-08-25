using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Components
{
    internal static class FieldStyleExtensions
    {
        /// <summary>自分に明示設定された背景色・文字色だけを style 文字列にする (コアの GetStyleString と同じ規約)。無ければ null。</summary>
        internal static string? GetOwnStyleString(this FieldBase field)
        {
            var drawStyle = field.GetOwnStyles();
            var style = string.Empty;
            if (drawStyle.HasFlag(DrawStyle.BackgroundColor)) style += $"background-color: {field.BackgroundColor};";
            if (drawStyle.HasFlag(DrawStyle.Color)) style += $"color: {field.Color};";
            return style.Length == 0 ? null : style;
        }
    }
}
