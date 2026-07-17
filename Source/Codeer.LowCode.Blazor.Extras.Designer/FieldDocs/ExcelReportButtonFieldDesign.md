## Design

詳細レイアウトに配置できる Excel 帳票ダウンロードボタンです。
スクリプトの Excel オブジェクトで典型的な「テンプレート Excel のプレースホルダを自モジュールの値で
置換してダウンロードする」処理を、スクリプトを書かずに実行できます。

### 動作

1. アプリのリソース (Resources) から `TemplateResourcePath` のテンプレート Excel を取得
2. テンプレート内の `{{フィールド名}}` プレースホルダを、ボタンが属するモジュールの値で置換
   (スクリプトの `Excel.OverWrite(this)` と同じ)
3. `Format` に従って xlsx または PDF でダウンロード
   (PDF 変換はスクリプトの Excel オブジェクトと同じ仕組み = Web アプリではサーバーの変換エンドポイント、
   デスクトップではホスト側フック)

セル単位の操作や複数モジュールの合成など、これを超える帳票はスクリプトの Excel オブジェクトで実装します。

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| TemplateResourcePath | string | ○ | テンプレート Excel のリソースパス |
| Format | enum | - | 出力形式。`Xlsx` (既定) / `Pdf` |
| DownloadFileName | string | - | ダウンロードファイル名 (拡張子は出力形式から自動付与)。空ならテンプレートのファイル名 |

### モジュール JSON 例

```json
{
  "TemplateResourcePath": "Reports/Quotation.xlsx",
  "Format": "Pdf",
  "Name": "QuotationReport",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ExcelReportButtonFieldDesign"
}
```

### PDF 出力の前提

`Format: Pdf` はサーバーの PDF 変換エンドポイント (`Excel.ConvertPdfEndPoint`、テンプレートで設定済み)
またはデスクトップのホスト側フック (`Excel.ConvertPdf`) が必要です (スクリプトの `DownloadPdf()` と同じ)。

## Script

### スクリプト API

このフィールドはスクリプト API を公開していません。スクリプトから同等の処理を行う場合は
Excel オブジェクト (`new Excel(...)` → `OverWrite` → `Download`/`DownloadPdf`) を使います。

## CSS

### CSS カスタマイズ

Bootstrap のボタン (`btn btn-outline-secondary`、アイコンのみ) として描画されます。
`data-system="excel-report"` 属性を持ちます。アイコンは出力形式に応じて Excel / PDF になります。
