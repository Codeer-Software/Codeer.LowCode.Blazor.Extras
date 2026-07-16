## Design

一覧ページの一括ダウンロード/一括更新のファイル形式を Excel (xlsx) から CSV に切り替える設定用フィールドです。
モジュールの Fields に定義するだけで有効になり、レイアウトへの配置は不要です (配置しても実行時は何も描画されません)。

### 機能

- **一括ダウンロードが CSV になる**: 一覧ページ (PageLink の `CanBulkDataDownload: true`) のダウンロードボタンが `{モジュール名}.csv` を出力
- **一括更新が CSV を受け付ける**: アップロードはファイル内容で自動判定するため、CSV と xlsx のどちらもアップロード可能
- **エンコーディング指定**: UTF-8 (BOM 付き / なし) と Shift_JIS を選択可能。既定は BOM 付き UTF-8 (Excel でダブルクリックしても文字化けしない)
- **区切り文字・最大行数の指定**
- **取込前検証**: 対応しない列名・型変換できないセルを行番号付きで報告し、エラーがあれば 1 行も取り込まない
- **このフィールドを定義しない場合は従来どおり xlsx**

このフィールドは**ファイル形式だけ**を切り替えます。列の並びや外部列名を相手仕様に合わせるのは
独立した機能 `MappedFileTransferField` で、**併用すると相手仕様の CSV** (WebEDI 等) になります
(Mapped 単独なら Excel のまま列だけ差し替わる)。

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| Encoding | enum | - | CSV のエンコーディング。`Utf8Bom` (既定) / `Utf8` / `ShiftJis` |
| Delimiter | string | - | 区切り文字。既定 `,`。タブは `\t` |
| FileExtension | string | - | ダウンロードのファイル拡張子。既定 `csv` (`txt` 等に変更可) |

### モジュール JSON 例

```json
{
  "Encoding": "Utf8Bom",
  "Name": "CsvTransfer",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.CsvFileTransferFieldDesign"
}
```

### サーバー側の対応 (必須)

クライアントはこのフィールドからダウンロードファイルの拡張子 (csv) を決め、
サーバーは同じモジュールデザインからこのフィールドを参照して生成/取り込みを分岐します。
サーバーテンプレートの `ModuleDataController` が対応済みである必要があります (未対応のサーバーでは
「.csv という名前の xlsx」が落ちてきます)。

テンプレートの実装 (`list_file` / `submit_by_file`) は処理本体を `BulkFileTransfer` に移譲しています:

```csharp
//一括ダウンロード/一括更新の処理本体は Extras の BulkFileTransfer に移譲
//(モジュールデザインの CsvFileTransferField の有無で CSV / Excel を分岐する)
[HttpPost("list_file")]
public async Task<IActionResult> GetListFileAsync(SearchCondition? condition)
    => Ok(await BulkFileTransfer.GetListFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, condition!));

[HttpPost("submit_by_file")]
public async Task<List<ModuleSubmitResult>> SubmitByFileAsync(string? moduleName)
    => await BulkFileTransfer.SubmitByFileAsync(DesignerService.GetDesignData(), _dataService.ModuleDataIO, moduleName, Request.Body);
```

`BulkFileTransfer` は `Codeer.LowCode.Blazor.Extras.Server.BulkFile` 名前空間、
CSV の生成/パース単体は `CsvUtils` (`Codeer.LowCode.Blazor.Extras.Server.Csv`) にあります
(いずれも Codeer.LowCode.Blazor.Extras.Server パッケージ)。
形式の追加や外部システム連携などの特殊実装は、テンプレート側で `BulkFileTransfer` を呼ばずに
独自処理へ差し替えることで行えます (モジュールデザインの具象フィールド型を参照して分岐できます)。

### CSV の仕様

- RFC 4180 準拠 (カンマ・引用符・改行を含むセルは引用符で囲み、引用符は二重化)。改行コードは CRLF
- 1 行目はヘッダ行 (xlsx の一括ダウンロードと同じ列構成)
- アップロード時、全セルが空の行は無視される

## Script

### スクリプト API

このフィールドは値を持たず、スクリプトから利用できる API を公開していません。

## CSS

### CSS カスタマイズ

実行時は何も描画しないため、CSS カスタマイズの対象はありません。
