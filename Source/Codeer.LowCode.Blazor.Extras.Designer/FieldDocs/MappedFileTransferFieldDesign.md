## Design

一覧ページの一括ダウンロード/一括更新の**列構成**を「相手仕様固定の列」(WebEDI・他システム連携など) に切り替える設定用フィールドです。
モジュールの Fields に定義するだけで有効になり、レイアウトへの配置は不要です (配置しても実行時は何も描画されません)。

列の並び・外部列名・書式・固定値・コード変換を宣言でき、サーバー側が内部形式との相互変換を行います。
**ファイル形式とは独立した機能**で、`CsvFileTransferField` との組み合わせで動作が決まります:

| 定義するフィールド | 一括ダウンロード/更新 |
|---|---|
| なし | xlsx (内部名ヘッダ) — 従来どおり |
| CsvFileTransferField のみ | CSV (内部名ヘッダ) |
| MappedFileTransferField のみ | **xlsx (相手仕様の列)** |
| 両方 | **CSV (相手仕様の列)** — WebEDI 向け |

### 機能

- **列マッピング**: ファイルの列位置 = マッピング定義の並び順。外部列名 (ヘッダ)・対応フィールド・書式・固定値を列ごとに指定
- **コード変換**: 変換表は**ただの業務モジュール** (例: 自社得意先コード⇔EDI取引先コード)。列ごとに変換モジュール名と外部/内部フィールド名を指定すると、出力時は内部→外部、取込時は外部→内部に引き当てる。引き当てられない外部コードは行番号付きエラー
- **書式**: 日付/数値の書式 (例 `yyyyMMdd`)。出力時は書式化、取込時は書式でパース
- **ヘッダ有無**: `HasHeader: false` でヘッダ行なしのファイルに対応
- エンコーディング・区切り文字・拡張子は CSV の関心事なので `CsvFileTransferField` 側で指定

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
| Format | 日付/数値の書式 (例 `yyyyMMdd`)。コード変換とは併用不可 (変換が優先) |
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
      { "ExternalName": "取引先", "Field": "", "Format": "", "FixedValue": "JP0001",
        "ConversionModule": "", "ConversionExternalField": "", "ConversionInternalField": "" },
      { "ExternalName": "得意先", "Field": "Customer.Value", "Format": "", "FixedValue": "",
        "ConversionModule": "EdiCustomerMap", "ConversionExternalField": "EdiCode", "ConversionInternalField": "CustomerCode" },
      { "ExternalName": "受注日", "Field": "OrderDate.Value", "Format": "yyyyMMdd", "FixedValue": "",
        "ConversionModule": "", "ConversionExternalField": "", "ConversionInternalField": "" }
    ]
  },
  "Name": "EdiMapping",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MappedFileTransferFieldDesign"
}
```

CSV にする場合は同じモジュールの Fields に `CsvFileTransferFieldDesign` も定義します (形式はそちらで指定):

```json
{
  "Encoding": "ShiftJis",
  "Delimiter": ",",
  "FileExtension": "txt",
  "Name": "EdiFormat",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.CsvFileTransferFieldDesign"
}
```

### コード変換表のモジュール

変換表は普通の業務モジュールとして作ります (テーブル + モジュール定義)。例: `EdiCustomerMap` に
`EdiCode` (外部コード) と `CustomerCode` (内部値) のフィールド。一覧・詳細画面での保守、権限、
変更履歴、Excel 一括メンテがそのまま使えます。転送時に全件読み込んで辞書にするため、
変換表は数千行程度までを想定してください。

### サーバー側の対応 (必須)

`CsvFileTransferField` と同じく、サーバーテンプレートの `ModuleDataController` が
`BulkFileTransfer` (Codeer.LowCode.Blazor.Extras.Server) に移譲済みである必要があります。
取込時は「対応しない列・型変換できないセル・引き当てられない外部コード」を行番号付きで報告し、
エラーがあれば 1 行も取り込みません。

## Script

### スクリプト API

このフィールドは値を持たず、スクリプトから利用できる API を公開していません。

## CSS

### CSS カスタマイズ

実行時は何も描画しないため、CSS カスタマイズの対象はありません。
