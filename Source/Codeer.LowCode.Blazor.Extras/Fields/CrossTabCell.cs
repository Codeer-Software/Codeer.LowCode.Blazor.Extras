namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// クロス表でクリックされたセル (OnCellClick の引数)。行・列の鍵 (値と表示) と、そのセルの集計値。
    /// 行計・列計・総計のセルでは対応する鍵が空 (RowKeys / ColumnKeys が空のリスト)。
    /// 明細の一覧へ遷移するときは RowKeyValues / ColumnKeyValues (丸めた後の値。日付は期間の開始日) を条件に使う。
    /// </summary>
    public class CrossTabCell
    {
        /// <summary>行の軸の値 (軸の順)。</summary>
        public List<object?> RowKeyValues { get; set; } = new();
        /// <summary>行の軸の表示文字。</summary>
        public List<string> RowKeyTexts { get; set; } = new();
        /// <summary>列の軸の値 (軸の順)。</summary>
        public List<object?> ColumnKeyValues { get; set; } = new();
        /// <summary>列の軸の表示文字。</summary>
        public List<string> ColumnKeyTexts { get; set; } = new();
        /// <summary>値の番号 (Measures の順)。</summary>
        public int MeasureIndex { get; set; }
        /// <summary>セルの集計値 (空なら null)。</summary>
        public object? Value { get; set; }
        /// <summary>行計のセルか。</summary>
        public bool IsRowTotal { get; set; }
        /// <summary>列計のセルか。</summary>
        public bool IsColumnTotal { get; set; }

        public string RowText => string.Join(" / ", RowKeyTexts);
        public string ColumnText => string.Join(" / ", ColumnKeyTexts);
    }
}
