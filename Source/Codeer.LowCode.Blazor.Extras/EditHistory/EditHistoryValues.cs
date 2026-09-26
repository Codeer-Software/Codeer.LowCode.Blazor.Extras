using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using R = Codeer.LowCode.Blazor.Extras.Properties.Resources;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// スナップショット中のフィールドデータから値と表示文字列を取り出す。
    /// 表示文字列は差分一覧 (旧 → 新) 用。候補・リンクは表示名、ファイルは名前、
    /// 日付・数値はデザインの書式 (IExternalTextFormatFieldDesign)。
    /// </summary>
    internal static class EditHistoryValues
    {
        internal static object? GetValue(FieldDataBase? data) => data switch
        {
            null => null,
            ValueFieldDataBase<string> s => s.Value,
            ValueFieldDataBase<decimal?> n => n.Value,
            ValueFieldDataBase<bool?> b => b.Value,
            ValueFieldDataBase<DateTime?> d => d.Value,
            ValueFieldDataBase<DateOnly?> d => d.Value,
            ValueFieldDataBase<TimeOnly?> t => t.Value,
            ValueFieldDataBase<DateTimeOffset?> d => d.Value,
            FileFieldData f => f.FileName,
            _ => null,
        };

        /// <summary>
        /// 「旧 → 新」の文字列にできる型か。文字列・数値・真偽・日付時刻・候補・リンク・ファイル (名前) だけ。
        /// それ以外 (独自のデータクラス) は変更の有無だけ出し、内容は「この版を表示」(本物のコンポーネント) に任せる。
        /// </summary>
        internal static bool IsTextSupported(FieldDataBase? data) => data is null
            or ValueFieldDataBase<string> or ValueFieldDataBase<decimal?> or ValueFieldDataBase<bool?>
            or ValueFieldDataBase<DateTime?> or ValueFieldDataBase<DateOnly?> or ValueFieldDataBase<TimeOnly?>
            or ValueFieldDataBase<DateTimeOffset?> or FileFieldData;

        /// <param name="designData">Select の enum の表示名を引くため (スナップショットの DisplayText は空のことがある)。</param>
        internal static string Format(FieldDesignBase? design, FieldDataBase? data, DesignData? designData = null)
        {
            switch (data)
            {
                case null:
                    return string.Empty;
                case SelectFieldData select:
                    if (!string.IsNullOrEmpty(select.DisplayText)) return select.DisplayText;
                    return EnumDisplayText(design, select.Value, designData) ?? select.Value ?? string.Empty;
                case LinkFieldData link:
                    return !string.IsNullOrEmpty(link.DisplayText) ? link.DisplayText : link.Value ?? string.Empty;
                case BooleanFieldData boolean:
                    return boolean.Value == null ? string.Empty : boolean.Value == true ? R.EditHistoryTrue : R.EditHistoryFalse;
                case FileFieldData file:
                    return file.FileName ?? string.Empty;
            }
            var value = GetValue(data);
            if (value == null) return string.Empty;
            if (design is IExternalTextFormatFieldDesign format) return format.FormatExternalText(value);
            return value.ToString() ?? string.Empty;
        }

        //enum 候補の Select: デザインの enum 定義から表示名を引く (無ければ null)
        static string? EnumDisplayText(FieldDesignBase? design, string? value, DesignData? designData)
        {
            if (design is not SelectFieldDesign select || string.IsNullOrEmpty(select.EnumName) || designData == null || value == null) return null;
            var enumDesign = designData.Enums.FirstOrDefault(e => e.Name == select.EnumName);
            return enumDesign?.FindMemberByValue(value)?.GetDisplayText(enumDesign.ValueType);
        }

        internal static string DisplayName(FieldDesignBase design)
            => design is IDisplayName d && !string.IsNullOrEmpty(d.DisplayName) ? d.DisplayName : design.Name;
    }
}
