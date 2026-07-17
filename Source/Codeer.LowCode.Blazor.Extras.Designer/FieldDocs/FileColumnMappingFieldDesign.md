## Design

一覧ページの一括ダウンロード/一括更新の**列構成**を「相手仕様固定の列」(WebEDI・他システム連携など) に切り替える設定用フィールドです。
モジュールの Fields に定義するだけで有効になり、レイアウトへの配置は不要です (配置しても実行時は何も描画されません)。

列の並び・外部列名・書式・固定値・コード変換を宣言でき、サーバー側が内部形式との相互変換を行います。
**ファイル形式とは独立した機能**で、`CsvFileFormatField` との組み合わせで動作が決まります:

| 定義するフィールド | 一括ダウンロード/更新 |
|---|---|
| なし | xlsx (内部名ヘッダ) — 従来どおり |
| CsvFileFormatField のみ | CSV (内部名ヘッダ) |
| FileColumnMappingField のみ | **xlsx (相手仕様の列)** |
| 両方 | **CSV (相手仕様の列)** — WebEDI 向け |

### 機能

- **列マッピング**: ファイルの列位置 = マッピング定義の並び順。外部列名 (ヘッダ)・対応フィールド・書式・固定値を列ごとに指定
- **コード変換**: 変換表は**ただの業務モジュール** (例: 自社得意先コード⇔EDI取引先コード)。列ごとに変換モジュール名と外部/内部フィールド名を指定すると、出力時は内部→外部、取込時は外部→内部に引き当てる。引き当てられない外部コードは行番号付きエラー
- **書式**: フィールド側の設定に従う。日付/日時/数値フィールドは自身の `Format` プロパティ (例 `yyyyMMdd`) で出力時は書式化、取込時はパースし、書式どおりでない値は行番号付きエラー。変換はフィールドデザインへの委譲 (`IExternalTextFormatFieldDesign`) で、和暦・右詰め空白埋めなどの特殊書式はこのインターフェースを実装した独自フィールドで対応する。EDI用に画面と別書式が必要な場合は連携用モジュール (ビュー) を分けてそちらのフィールドに書式を設定する
- **ヘッダ有無**: `HasHeader: false` でヘッダ行なしのファイルに対応
- エンコーディング・区切り文字・拡張子は CSV の関心事なので `CsvFileFormatField` 側で指定

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| HasHeader | bool | - | ヘッダ行の有無。既定 true |
| Columns | MappingColumns | ○ | 列マッピング (並び順 = ファイルの列位置)。専用エディタで編集 |

### Columns (MappingColumn) の項目

| 項目 | 説明 |
|---|---|
| ExternalName | 外部ファイルでの列名 (HasHeader 時にヘッダへ出力。取込は列位置で対応付け) |
| Field | 対応する内部フィールド (`フィールド名.データメンバ名`。例 `Customer.Value`)。空なら取込時は無視、出力時は FixedValue |
| FixedValue | 出力時の固定値 (Field が空の列で使う。取引先コード等) |
| ConversionModule | コード変換表のモジュール名 (空なら変換なし) |
| ConversionExternalField | 変換表の外部コード側フィールド名 (例 `EdiCode`) |
| ConversionInternalField | 変換表の内部値側フィールド名 (例 `CustomerCode`) |

### モジュール JSON 例 (WebEDI = CSV 併用)

```json
{
  "HasHeader": false,
  "Columns": {
    "Items": [
      { "ExternalName": "取引先", "Field": "", "FixedValue": "JP0001",
        "ConversionModule": "", "ConversionExternalField": "", "ConversionInternalField": "" },
      { "ExternalName": "得意先", "Field": "Customer.Value", "FixedValue": "",
        "ConversionModule": "EdiCustomerMap", "ConversionExternalField": "EdiCode", "ConversionInternalField": "CustomerCode" },
      { "ExternalName": "受注日", "Field": "OrderDate.Value", "FixedValue": "",
        "ConversionModule": "", "ConversionExternalField": "", "ConversionInternalField": "" }
    ]
  },
  "Name": "EdiMapping",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.FileColumnMappingFieldDesign"
}
```

受注日を `20260717` のような書式で入出力するには、`OrderDate` フィールド (DateField) 側の `Format` に
`yyyyMMdd` を設定します (書式は列ではなくフィールドの設定)。

CSV にする場合は同じモジュールの Fields に `CsvFileFormatFieldDesign` も定義します (形式はそちらで指定):

```json
{
  "Encoding": "ShiftJis",
  "Delimiter": "Comma",
  "FileExtension": "txt",
  "Name": "EdiFormat",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.CsvFileFormatFieldDesign"
}
```

### コード変換表のモジュール

変換表は普通の業務モジュールとして作ります (テーブル + モジュール定義)。例: `EdiCustomerMap` に
`EdiCode` (外部コード) と `CustomerCode` (内部値) のフィールド。一覧・詳細画面での保守、権限、
変更履歴、Excel 一括メンテがそのまま使えます。転送時に全件読み込んで辞書にするため、
変換表は数千行程度までを想定してください。

### サーバー側の対応 (必須)

`CsvFileFormatField` と同じく、サーバーテンプレートの `ModuleDataController` が
`BulkFileTransfer` (Codeer.LowCode.Blazor.Extras.Server) に移譲済みである必要があります。
変換はテーブルテキストを経由せず、フィールドの型付きの値 (ModuleData) と外部列を直接相互変換します。
取込時は「書式どおりに解釈できない値・型変換できない値・引き当てられない外部コード」を行番号付きで報告し、
エラーがあれば 1 行も取り込みません。

## Script

### スクリプト API

このフィールドは値を持たず、スクリプトから利用できる API を公開していません。

## CSS

### CSS カスタマイズ

実行時は何も描画しないため、CSS カスタマイズの対象はありません。
