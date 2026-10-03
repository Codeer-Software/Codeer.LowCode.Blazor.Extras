using Codeer.LowCode.Blazor.Aggregation;
﻿using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// クロス表のセルと軸の見出しの文字。見た目は元のフィールドの設計に従う:
    /// 値は 値の Format → 元の項目 (NumberField) の Format → 既定 (桁区切り・小数は 2 桁まで) の順。件数・重複を除いた件数は整数。
    /// 軸は 真偽が TrueText / FalseText、数値が項目の Format。日付は単位に合わせた見出し (CrossTabKeyText)。
    /// UTC で保存した日時・時刻 (SaveAsUtc) はローカル時刻で出す。
    /// </summary>
    public static class CrossTabFormatter
    {
        const string DefaultNumberFormat = "#,##0.##";

        /// <summary>集計値の文字 (空は空文字)。</summary>
        public static string Measure(MultiTypeValue value, AggregateMeasure measure, FieldDesignBase? sourceField, CultureInfo culture)
        {
            var v = value.GetValue();
            if (v == null) return string.Empty;
            if (measure.Function is AggregateFunction.Count or AggregateFunction.CountDistinct)
                return v is decimal count ? count.ToString("N0", culture) : Convert.ToString(v, culture) ?? string.Empty;
            var format = !string.IsNullOrEmpty(measure.Format) ? measure.Format : (sourceField as NumberFieldDesign)?.Format ?? string.Empty;
            return v switch
            {
                decimal d => Number(d, format, culture),
                DateOnly d => d.ToString("yyyy-MM-dd", culture),
                DateTime d => ToLocal(d, sourceField).ToString("yyyy-MM-dd HH:mm", culture),
                TimeOnly t => ToLocal(t, sourceField).ToString("HH:mm", culture),
                _ => Convert.ToString(v, culture) ?? string.Empty,
            };
        }

        //UTC で保存した項目 (SaveAsUtc) は画面の項目と同じくローカル時刻で出す
        static DateTime ToLocal(DateTime value, FieldDesignBase? sourceField)
            => sourceField is DateTimeFieldDesign { SaveAsUtc: true } ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime() : value;

        static TimeOnly ToLocal(TimeOnly value, FieldDesignBase? sourceField)
            => sourceField is TimeFieldDesign { SaveAsUtc: true } ? TimeOnly.FromDateTime(new DateTime(new DateOnly(2000, 1, 1), value, DateTimeKind.Utc).ToLocalTime()) : value;

        /// <summary>軸の値の文字 (表示名があればそれ、空値は emptyText)。</summary>
        public static string Key(AggregateKey key, AggregateGroup? group, FieldDesignBase? sourceField, string emptyText, string trueText, string falseText, CultureInfo culture)
        {
            if (!string.IsNullOrEmpty(key.DisplayText)) return key.DisplayText;
            var value = key.Value.GetValue();
            if (value == null) return emptyText;
            if (value is DateOnly date && group is DateGroup dateGroup)
            {
                return dateGroup.Bucket switch
                {
                    DateBucket.Year => CrossTabKeyText.Year(date, dateGroup.FiscalYearStartMonth),
                    DateBucket.Quarter => CrossTabKeyText.Quarter(date, dateGroup.FiscalYearStartMonth),
                    DateBucket.Month => date.ToString("yyyy-MM", culture),
                    _ => date.ToString("yyyy-MM-dd", culture),
                };
            }
            //「時」でまとめた軸は SQL が時差を足して丸め済なので、そのまま
            if (value is DateTime dateTime) return (group is DateGroup ? dateTime : ToLocal(dateTime, sourceField)).ToString("yyyy-MM-dd HH:mm", culture);
            if (value is TimeOnly time) return ToLocal(time, sourceField).ToString("HH:mm", culture);
            if (value is bool b)
            {
                var boolean = sourceField as BooleanFieldDesign;
                var text = b ? boolean?.TrueText : boolean?.FalseText;
                return !string.IsNullOrEmpty(text) ? text : b ? trueText : falseText;
            }
            if (value is decimal d) return Number(d, (sourceField as NumberFieldDesign)?.Format ?? string.Empty, culture, defaultFormat: null);
            return Convert.ToString(value, culture) ?? string.Empty;
        }

        //書式が不正なら既定で出す (設計の書式の誤りで表が壊れないように)
        static string Number(decimal d, string format, CultureInfo culture, string? defaultFormat = DefaultNumberFormat)
        {
            if (string.IsNullOrEmpty(format)) return defaultFormat == null ? d.ToString(culture) : d.ToString(defaultFormat, culture);
            try { return d.ToString(format, culture); }
            catch (FormatException) { return d.ToString(DefaultNumberFormat, culture); }
        }
    }
}
