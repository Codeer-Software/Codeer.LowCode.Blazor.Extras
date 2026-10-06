## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.CrossTabFieldDesign`

クロス集計表。元モジュール (`SearchCondition.ModuleName`) の行を、行の項目 × 列の項目 で件数・合計・平均・最小・最大・重複を除いた件数に集計して表にする。「月別 × 状態別の受注金額」「担当者ごとの件数」のように、SQL を書かずに集計を見せたいときに使う。集計はサーバーで行い (DB の GROUP BY)、権限は一覧と同じ (読めない行は数えない・読めない項目は使えない)。行ごとの合計・列ごとの合計・総計は項目を減らした集計を別に実行して出すので、平均や重複を除いた件数も正しい。表は「集計定義を受け取って描くだけ」の部品で、定義は設計 (Setting) か、スクリプトの `Show(ModuleAggregator)` で渡す。`CanCustomize` を付けると、閲覧者が画面で行・列・値を自分用に組み替えられる。

> **いつ使う**: 集計の数字を表で見せるならこれ。グラフで見せるなら ApexCharts のチャートフィールド (値は本フィールドと同じ集計をスクリプトの `ModuleAggregator` で取って渡す)。明細を並べるなら ListField。自由な SQL が要る集計 (任意の結合・ウィンドウ関数) は QueryField。

> **DB を持つモジュールだけ**。QueryField で定義したモジュールは元モジュールにできない。結合はリンク (多対一) をたどるだけ (`Customer.Region.Value` のようにリンク越しの項目を行・列・値・条件に使える。設計にドット列を置かなくてよい)。一対多 (子一覧) は子を元モジュールにして親で分類する (得意先ごとの受注合計 = Order を元に `Customer.Value` で分類)。

### C# クラス定義 (真実の源)

```csharp
public class CrossTabFieldDesign : FieldDesignBase, IDisplayName, ISearchResultsViewFieldDesign, IFillHeightFieldDesign
{
    public SearchCondition SearchCondition { get; set; }          // 元モジュールと条件
    public string DisplayName { get; set; }                       // 表示名 (連携したラベルが表示する。表自身は出さない)
    public CrossTabSetting Setting { get; set; }                  // 行・列・値・値で絞り込み・並べ替え・表示件数の上限 (元モジュールが空ならスクリプトの Show 専用)
    public bool ShowRowTotals { get; set; } = true;
    public bool ShowColumnTotals { get; set; } = true;
    public bool ShowGrandTotal { get; set; } = true;
    public CrossTabValueDisplay ValueDisplay { get; set; }        // Value / PercentOfTotal / PercentOfRow / PercentOfColumn (値 / 総計・行の合計・列の合計 に対する割合)
    public bool CanCustomize { get; set; }                        // 利用者が集計 (行・列・値・絞り込み・並べ替え・上限・値の表示形式) を自分用に変えられる
    public string OnCellClick { get; set; }                       // セルをクリックしたとき (引数 CrossTabCell)
}

public class CrossTabSetting
{
    public List<AggregateGroup> Rows { get; set; }                // 行の項目
    public List<AggregateGroup> Columns { get; set; }             // 列の項目 (空なら行だけの表)
    public List<AggregateMeasure> Measures { get; set; }          // 値 (1 つ以上)
    public List<AggregateHaving> Having { get; set; }             // 値で絞り込み (値の番号・比較・値)
    public List<AggregateSort> SortConditions { get; set; }       // 並べ替え (Group / Measure の番号・降順)
    public int? LimitCount { get; set; }                          // 表示件数の上限 (行の項目の数。行が無ければ列の項目)
}
// 軸 (まとめ方) は型で表す。JSON は条件やフィールドと同じく TypeFullName で型を持つ
public abstract class AggregateGroup { public string Variable; }
public class ValueGroup : AggregateGroup { }                                               // 値そのまま (文字・選択・リンク・真偽・数値)
public class DateGroup : AggregateGroup { public DateBucket Bucket; public int FiscalYearStartMonth = 1; }  // 日付・日時を単位でまとめる。Bucket: Year / Quarter / Month / Week / Day / Hour。FiscalYearStartMonth は Year / Quarter の年度の開始月
public class AggregateMeasure { public AggregateFunction Function; public string Variable; public string Name; public string Format; }  // Count / CountDistinct / Sum / Avg / Min / Max。Format は表示の書式 (.NET の数値の書式 "N1" / "P0" / "C0"。空なら元の項目の Format)
public class AggregateHaving { public int MeasureIndex; public MatchComparison Comparison; public decimal Value; }
public class AggregateSort { public AggregateSortTarget Target; public int Index; public bool IsDescending; }
```

### 設定の要点

- `Variable` は検索条件と同じ変数名 (`Status.Value`、リンク越しは `Customer.Region.Value`)。`Count` の `Variable` は空
- `Sum` / `Avg` は数値項目、`Min` / `Max` は数値・日付・日時・文字、`CountDistinct` はどの項目でも
- 日付の丸め: 年と四半期は軸ごとの `FiscalYearStartMonth` (年度の開始月 1〜12。既定 1 = 暦年) で切る。4 なら 4 月〜翌 3 月が 1 年度で、見出しは「2026年度」「2026年度 Q1」(暦年は「2026」「2026 Q2」)。同じ表に年度の軸と暦年の軸を置ける。週は月曜始まり。`SaveAsUtc` の日時はローカル時刻に直してから丸める
- 選択・リンクの項目は値 (コード) で分類し、表示名 (候補値の名前・リンク先の表示項目) が表に出る。空値は「(空白)」の 1 グループ。真偽の項目は `TrueText` / `FalseText`、数値の項目は `Format` で出る (元のフィールドの設計に従う)
- 軸の並び: 候補つき選択は候補の順・リンクは表示名の順・日付と数値は値の順。空値は最後。UTC 保存の日時はブラウザの時差で区切る
- 値の表示は 値の `Format` → 元の項目 (NumberField) の `Format` → 既定 (桁区切り・小数 2 桁まで) の順。件数・重複を除いた件数は整数。確度を `P0` で設計していれば平均も「40%」で出る。表全体の小数桁の設定は無い。エディタの書式欄は候補 (`N0` 整数 / `N1` 小数 1 桁 / `N2` / `P0` パーセント / `P1` / `C0` 通貨) から選ぶか自由に入力する
- `Having` と `SortConditions` の番号は 0 始まり。`SortConditions` の Group の番号は Rows → Columns の順
- 表のセル (行の種類 × 列の種類 × 値の数) が 20,000 を超えると表を描かずにエラーを出す (日単位 × 顧客 のような細かすぎる組み合わせの暴走止め)。集計の件数自体に上限は無い
- `Having`・値 (Measure) の並べ替え・`LimitCount` は**行の項目**に掛かり、その行の合計 (行の軸だけで集計し直した値。平均・重複を除いた件数も正しい) で評価する (Excel のピボットと同じ)。列の項目は絞らず全部出し、項目の順に並べる (Group の並べ替えで列の項目の昇順・降順は指定できる)。列が無ければ行、行が無ければ列に掛かる
- 列ごとの合計・総計・割合の分母は表に出ている行の分 (表が縦に足し合う)。全データを分母にした構成比は出せない
- 上限で切れると表の下に「条件に合う n 件・全 m 行のうち k 行を表示」の注意が出る (黙って欠けない)
- 検索フィールド (SearchFieldDesign) の `ResultsViewFieldName` にこのフィールドを指定すると、検索条件で絞った行だけを集計する。一覧ページの表示 (ListPageDesign) にはできない
- `SearchCondition.ModuleName` を空にしておくと設計では何も集計せず、スクリプトの `Show(aggregator)` で渡した定義だけを表示する (条件で集計の中身を切り替えたいとき)
- 割合表示は件数 (Count) と合計 (Sum) にだけ掛かる (平均・最小・最大・重複を除いた件数は値のまま)。分母にした合計のセル (行の合計に対する割合なら右端の合計列、列の合計に対する割合なら下端の合計行、総計) は 100% になる。他の合計セルは総計に対する割合
- `CanCustomize: true` にすると、利用者が表の右上の「集計のカスタマイズ」ボタン (集計がエラーのときも出る。またはスクリプトの `ShowCustomDialog()`) で集計を自分用に変えられる。保存先はブラウザ (localStorage。キーは「モジュール名.フィールド名」で ListField のカラムカスタマイズと同じ)。共通にしたい集計は設計 (Setting) に書く。設計が変わって保存内容が合わなくなったら、無い項目を捨てて合わせる (値が残らなければ設計どおり)。元モジュールと絞り込み条件・合計の表示は変えられない。スクリプトの `Show` で定義を渡している表ではカスタマイズしない
- 表は与えられた幅と高さの中で内部スクロールし、見出し・行見出し・合計行は固定される。幅はレイアウトのカラムに従い、表の広さでページを広げない

### JSON 例 (状態 × 月 の受注金額と件数。完了の金額が多い順)

```json
{
  "Name": "Summary",
  "SearchCondition": { "ModuleName": "Order", "LimitCount": null },
  "DisplayName": "受注サマリー",
  "Setting": {
    "Rows": [ { "Variable": "Status.Value", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Match.ValueGroup" } ],
    "Columns": [ { "Variable": "OrderedOn.Value", "Bucket": "Month", "FiscalYearStartMonth": 1, "TypeFullName": "Codeer.LowCode.Blazor.Repository.Match.DateGroup" } ],
    "Measures": [
      { "Function": "Sum", "Variable": "Amount.Value", "Name": "金額", "Format": "" },
      { "Function": "Count", "Variable": "", "Name": "件数", "Format": "" }
    ],
    "Having": [],
    "SortConditions": [ { "Target": "Measure", "Index": 0, "IsDescending": true } ],
    "LimitCount": null
  },
  "ShowRowTotals": true, "ShowColumnTotals": true, "ShowGrandTotal": true,
  "ValueDisplay": "Value", "CanCustomize": false,
  "OnCellClick": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.CrossTabFieldDesign"
}
```

### 注意事項

- 元モジュールの行の条件 (DataReadCondition)・モジュールの閲覧条件・項目の読み取り権限が効く。読めない項目を行・列・値・条件に使うと権限エラー (結果を黙って変えない)
- リンク先の行の条件は効かない (一覧のリンク表示値と同じ)。隠したいなら親側の PermissionField で
- 集計はホストの集計 API を通る。古いホスト (アプリテンプレート更新前) では「ホスト未対応」のエラーになる
- SQLite では日付の列を DATE / DATETIME で宣言する。TEXT で宣言した列は日付の単位 (月・四半期・年など) でまとめられず、セルクリックの明細の絞り込みも効かない

## Script

### スクリプト API

```csharp
public class CrossTabField
{
    public CrossTab? Table { get; }                 // 今の表 (Rows / Columns / Cells[値][行][列] / RowTotals / ColumnTotals / GrandTotals)
    public string LoadError { get; }               // 読み込み失敗のメッセージ
    public bool AllowLoad { get; set; }
    public Task Reload();                                            // 集計し直す
    public Task SetAdditionalCondition(ModuleSearcher searcher);     // 追加の条件 (設計の条件と AND)
    public Task Show(ModuleAggregator aggregator);                   // スクリプトで組んだ集計定義を表示 (グループは全部が行)
    public Task Show(ModuleAggregator aggregator, int rowCount);     // 先頭 rowCount 個のグループを行に、残りを列に
    public Task ShowCustomDialog();                                  // 集計のカスタマイズを開く (CanCustomize のときだけ)
}
public class CrossTabCell   // OnCellClick の引数
{
    public List<object?> RowKeyValues; public List<string> RowKeyTexts;
    public List<object?> ColumnKeyValues; public List<string> ColumnKeyTexts;
    public int MeasureIndex; public object? Value; public bool IsRowTotal; public bool IsColumnTotal;
    public string RowText; public string ColumnText;
    public ModuleSearcher CreateSearcher();   // このセルに数えた行の条件 (表の条件 + 行・列の鍵。日付は期間の範囲・空値は空値の行・合計のセルは表に出ている行の分)
}
```

同じ集計をスクリプトで取るには `ModuleAggregator<Order>` (ModuleSearcher の兄弟): `GroupBy` / `GroupByMonth` / `Count` / `Sum` / `HavingGreaterThanOrEqual` / `OrderByMeasureDescending` / `Limit` / `Execute()` → `AggregateResult` (`Rows[i].Key(0)` / `KeyText(0)` / `Number(1)`)。

### 使用例

```csharp
// スクリプトで組んだ集計を表示する (元モジュールを空にしたフィールドに)。先頭 1 つ (担当者) を行、残り (年度) を列に
var agg = new ModuleAggregator<Order>();
agg.GroupBy(m => m.Owner);
agg.GroupByYear(m => m.OrderedOn);
agg.Sum(m => m.Amount);
agg.Count("件数");
Summary.Show(agg, 1);

// 条件を切り替えて集計し直す
var searcher = new ModuleSearcher<Order>();
searcher.AddGreaterThanOrEqual(m => m.OrderedOn.Value, Period.Value);
Summary.SetAdditionalCondition(searcher);

// セルをクリックしたら、そのセルに数えた明細を同じ画面の一覧 (元モジュールと同じモジュールの ListField) に出す。
// 条件はセルが作るので、行・列の項目や日付の単位を書かなくてよい (利用者が行・列を変えても正しい)
void Summary_OnCellClick(CrossTabCell cell)
{
    Details.SetAdditionalCondition(cell.CreateSearcher());
    Details.Reload();   // ListField の SetAdditionalCondition は条件を入れるだけなので読み直す
}
// 行・列にリンク越しの項目 ("Customer.Region.Value" 等) を置いた表では、その項目 (ドット列) を一覧のモジュールにも置く (一覧の検索は設計に無いリンク越しの項目を条件に使えない。置いていないと CreateSearcher() が「「顧客 / 業種」で分けた表からは明細を表示できません。(… モジュールに 顧客Id.業種Id を追加)」の例外になる。利用者のカスタマイズで選ばれる項目も同じ)

// グラフに渡す
var agg = new ModuleAggregator<Order>();
agg.GroupByMonth(m => m.OrderedOn);
agg.Sum(m => m.Amount);
var result = agg.Execute();
```
