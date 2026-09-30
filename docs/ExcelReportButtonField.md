# ExcelReportButtonField - Excel 帳票ボタン

詳細レイアウトに置く、Excel 帳票のダウンロードボタンです。
アプリのリソースに置いたテンプレート Excel のセルを、ボタンが属するモジュールの値で置き換えて、xlsx または PDF でダウンロードします。
スクリプトの Excel オブジェクトで書く「テンプレートに値を書き込んでダウンロード」(`new Excel(...)` → `OverWrite(this)` → `Download()` / `DownloadPdf()`) を、スクリプトなしで行うフィールドです。

## 機能

- ボタン 1 つで帳票をダウンロード (アイコンは出力形式に応じて Excel / PDF)
- テンプレートは Excel で作る。値を入れたいセルに `$フィールド名.Value` と書いておくだけ
- 出力形式は xlsx / PDF から選択。PDF はスクリプトの `DownloadPdf()` と同じ仕組みで変換する
- 押したときの画面の値 (保存前の編集中の値を含む) が書き込まれる

セル単位の操作や、複数モジュールを合成する帳票など、これを超えるものはスクリプトの Excel オブジェクトで作ります。

## デザイナー設定プロパティ

| プロパティ | デザイナ表示名 | 型 | 必須 | 説明 |
|---|---|---|---|---|
| TemplateResourcePath | テンプレートリソースパス | string | ○ | テンプレート Excel のリソースパス (デザインプロジェクトの `Resources` からの相対パス。例 `Reports/Quotation.xlsx`) |
| Format | 出力形式 | enum | - | `Xlsx` (Excel (xlsx)。既定) / `Pdf` (PDF) |
| DownloadFileName | ダウンロードファイル名 | string | - | ダウンロードファイル名。拡張子は出力形式から付く (`.xlsx` / `.pdf`)。空ならテンプレートのファイル名 |

`TemplateResourcePath` が空だとデザインチェックが指摘します (`ExcelReportButtonFieldDesign:1`)。
実行時にリソースが見つからないときはエラーを表示して何もダウンロードしません。

## テンプレートの書き方

置換はセル単位です。**セルの内容全体**が `$` で始まるとき、`$` の後ろをモジュールからの参照として解決し、そのセルを値で置き換えます。

| セルの内容 | 書き込まれる値 |
|---|---|
| `$Title.Value` | Title フィールドの値 |
| `$OrderDate.Value` | 日付の値 (セルの表示形式はテンプレート側で設定する) |
| `$Customer.DisplayText` | リンクフィールドの表示文字列 |

- 参照は `.` 区切りで、スクリプトで `this` から辿れるメンバーと同じ名前を書きます (`フィールド名.Value` が基本形)
- 数値・日付は値のまま書き込まれます。桁区切りや日付の書式はテンプレートのセルの表示形式で決めます
- 解決できない参照のセルは書き換えずにそのまま残ります
- 「見積番号: $Id.Value」のように文字と混ぜたセルは置き換わりません。ラベルと値はセルを分けてください
- すべてのシートが対象です

置換は Excel 帳票ライブラリ [Excel.Report.PDF](https://github.com/Codeer-Software/Excel.Report.PDF) のテンプレート機能で行われます。
`#` で始まるセル (`#Page` / `#PageCount` などのページ番号、`#LoopRow` などの繰り返し) もこのライブラリの記法に従って処理されます。詳細は同ライブラリの README (Cell directive reference) を参照してください。

## モジュール JSON 例

```json
{
  "TemplateResourcePath": "Reports/Quotation.xlsx",
  "Format": "Pdf",
  "DownloadFileName": "見積書",
  "Name": "QuotationReport",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ExcelReportButtonFieldDesign"
}
```

## PDF 出力に必要な設定

`Format` を `Pdf` にすると、テンプレートに値を書き込んだ xlsx を PDF に変換してからダウンロードします。変換はスクリプトの `Excel.DownloadPdf()` と同じ経路です。
新しいアプリテンプレートには次がすべて入っています。

**Web アプリ (Blazor WebAssembly)**: サーバーで変換します。

1. サーバーの変換 API (テンプレートの `ExcelController`)

   ```csharp
   [ApiController]
   [Route("api/excel")]
   public class ExcelController : ControllerBase
   {
       [HttpPost("pdf")]
       public async Task<IActionResult> ConvertToPdfAsync()
       {
           using var memoryStream = new MemoryStream();
           await Request.Body.CopyToAsync(memoryStream);
           memoryStream.Position = 0;
           return Ok(ExcelConverter.ConvertToPdf(memoryStream));
       }
   }
   ```

2. クライアントの起動時にエンドポイントを設定 (テンプレートは `ServiceInitializer`)

   ```csharp
   Codeer.LowCode.Blazor.Extras.ScriptObjects.Excel.ConvertPdfEndPoint = "api/excel/pdf";
   ```

3. サーバーの `Program.cs` で PDF 用のフォントを設定する。Extras.Server の `CustomFontResolver` は、appsettings の `FontFileDirectory` のフォルダから `<フォント名>.ttf` を探し、無ければ `NotoSansJP.ttf` を使います。サーバーにフォントファイルを置いてください

   ```csharp
   GlobalFontSettings.FontResolver = new CustomFontResolver(SystemConfig.Instance.FontFileDirectory);
   ```

**デスクトップアプリ (WPF / WinForms)**: 同じプロセスで変換します。起動時に変換関数を設定し (`Excel.ConvertPdf = ExcelConverter.ConvertToPdf;`)、フォントリゾルバも同様に設定します。`ConvertPdf` が設定されていればエンドポイントは使いません。

どちらも未設定のときは PDF がダウンロードされません。

## スクリプト API

このフィールドはスクリプト API を公開していません。スクリプトから同じ処理をする場合や、セル単位で書き込みたい場合は Excel オブジェクトを使います。

```csharp
using var stream = Resources.GetMemoryStream("Reports/Quotation.xlsx");
using var excel = new Excel(stream, "見積書.xlsx");
excel.OverWrite(this);
excel.DownloadPdf();
```

## CSS カスタマイズ

Bootstrap のボタン (`btn btn-outline-secondary`、アイコンのみ) として描画され、`data-system="excel-report"` 属性を持ちます。
