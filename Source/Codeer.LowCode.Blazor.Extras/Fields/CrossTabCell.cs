using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// クロス表でクリックされたセル (OnCellClick の引数)。行・列の鍵 (値と表示) と、そのセルの集計値。
    /// 行計・列計・総計のセルでは対応する鍵が空 (RowKeys / ColumnKeys が空のリスト)。
    /// 明細の一覧へ遷移するときは CreateSearcher() (そのセルに数えた行の条件) を一覧に渡す。利用者が行・列を変えていても、そのときの表の項目で条件を作る。
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

        /// <summary>集計した元モジュール。</summary>
        [ScriptHide]
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>このセルに数えた行の条件 (表の条件 + 行・列の鍵。日付は期間の範囲、空値は空値の行)。合計のセルで行を絞り込み・上限で選んでいれば表に出ている行の分。</summary>
        [ScriptHide]
        public MatchConditionBase? Condition { get; set; }

        [ScriptHide]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        /// <summary>このセルの条件では明細を読めない理由 (読めるなら null)。リンク越しの項目で分けた表で、その項目を元モジュールに置いていないとき。</summary>
        [ScriptHide]
        public string? DetailError { get; set; }

        /// <summary>このセルに数えた行を読む ModuleSearcher (元モジュール)。一覧の SetAdditionalCondition に渡して Reload すれば明細の一覧になる。</summary>
        public ModuleSearcher CreateSearcher()
        {
            //一覧の検索で「フィールドが存在しません」になる前に、何をすればよいかが分かる文言で止める
            if (!string.IsNullOrEmpty(DetailError)) throw new LowCodeException(DetailError);
            var searcher = new ModuleSearcher(ModuleName) { Services = Services };
            if (Condition != null) searcher.AddConditions(Condition.JsonClone());
            return searcher;
        }
    }
}
