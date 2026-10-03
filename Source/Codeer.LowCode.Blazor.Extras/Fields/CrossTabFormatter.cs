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
                DateTime d => d.ToString("yyyy-MM-dd HH:mm", culture),
                TimeOnly t => t.ToString("HH:mm", culture),
                _ => Convert.ToString(v, culture) ?? string.Empty,
            };
        }

        /// <summary>軸の値の文字 (表示名があればそれ、空値は emptyText)。</summary>
        public static string Key(AggregateKey key, AggregateGroup? group, FieldDesignBase? sourceField, string emptyText, string trueText, string falseText, CultureInfo culture)
        {
            if (!string.IsNullOrEmpty(key.DisplayText)) return key.DisplayText;
            var value = key.Value.GetValue();
            if (value == null) return emptyText;
            if (value is DateOnly date && group != null)
            {
                return group.DateBucket switch
                {
                    DateBucket.Year => CrossTabKeyText.Year(date, group.FiscalYearStartMonth),
                    DateBucket.Quarter => CrossTabKeyText.Quarter(date, group.FiscalYearStartMonth),
                    DateBucket.Month => date.ToString("yyyy-MM", culture),
                    _ => date.ToString("yyyy-MM-dd", culture),
                };
            }
            if (value is DateTime dateTime) return dateTime.ToString("yyyy-MM-dd HH:mm", culture);
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
