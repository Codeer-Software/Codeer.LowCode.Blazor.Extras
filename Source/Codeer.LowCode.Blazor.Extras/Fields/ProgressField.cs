using System.Globalization;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class ProgressField(ProgressFieldDesign design) : FieldBase<ProgressFieldDesign>(design)
    {
        // ColorField / BarColor が未設定のときのバー色 (Gantt と同じ既定色)。
        internal const string DefaultBarColor = "#1a73e8";

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task OnExternalFieldChangedAsync(string fieldName)
        {
            await Task.CompletedTask;
            if (fieldName == Design.ValueField || fieldName == Design.ColorField)
                NotifyStateChanged();
        }

        /// <summary>参照フィールドから取得した進捗値。</summary>
        internal decimal? SourceValue => ReadDecimal(Design.ValueField);

        /// <summary>0〜100(%) の充填率。Scale を適用し、範囲外はクランプする。</summary>
        internal double FillPercent => Design.Scale.ToFillPercent(SourceValue);

        /// <summary>有効なバー色。ColorField &gt; BarColor の順で解決 (空なら空文字)。</summary>
        internal string EffectiveBarColor
        {
            get
            {
                var fromField = ReadString(Design.ColorField);
                return !string.IsNullOrEmpty(fromField) ? fromField : Design.BarColor;
            }
        }

        /// <summary>バー色に対して見やすい文字色 (Gantt と同じ YIQ コントラスト)。</summary>
        internal string TextColor
        {
            get
            {
                var color = EffectiveBarColor;
                if (string.IsNullOrEmpty(color)) color = DefaultBarColor;
                return ComputeContrastColor(color);
            }
        }

        /// <summary>メーター中央の数値色。100% 時のバーと同じ色 (EffectiveBarColor / 既定色)。</summary>
        internal string MeterNumberColor => string.IsNullOrEmpty(EffectiveBarColor) ? DefaultBarColor : EffectiveBarColor;

        private decimal? ReadDecimal(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return null;
            return (Module.GetField(fieldName)?.GetData() as ValueFieldDataBase<decimal?>)?.Value;
        }

        private string ReadString(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            return (Module.GetField(fieldName)?.GetData() as ValueFieldDataBase<string>)?.Value ?? string.Empty;
        }

        // YIQ ベースのコントラスト色: 明るい背景には濃い文字、暗い背景には白文字。
        private static string ComputeContrastColor(string color)
        {
            if (!TryParseHexColor(color, out var r, out var g, out var b)) return "#1a1a1a";
            var y = (r * 299 + g * 587 + b * 114) / 1000;
            return y >= 128 ? "#1a1a1a" : "#ffffff";
        }

        private static bool TryParseHexColor(string color, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (string.IsNullOrEmpty(color) || color[0] != '#') return false;
            var hex = color.AsSpan(1);
            if (hex.Length == 3)
            {
                if (!byte.TryParse(hex.Slice(0, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rr)) return false;
                if (!byte.TryParse(hex.Slice(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gg)) return false;
                if (!byte.TryParse(hex.Slice(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bb)) return false;
                r = rr * 17; g = gg * 17; b = bb * 17;
                return true;
            }
            if (hex.Length == 6)
            {
                if (!byte.TryParse(hex.Slice(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rr)) return false;
                if (!byte.TryParse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var gg)) return false;
                if (!byte.TryParse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var bb)) return false;
                r = rr; g = gg; b = bb;
                return true;
            }
            return false;
        }
    }
}
