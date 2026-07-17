## Design

詳細レイアウトに配置できる一括ダウンロード/一括更新 (アップロード) ボタンです。
一覧ページの一括ダウンロード/一括更新ボタンと同じサーバー処理 (`list_file` / `submit_by_file`) を利用するため、
ファイル形式や列構成は対象モジュールの `CsvFileFormatField` / `FileColumnMappingField` の定義に従います
(どちらも未定義なら従来どおり xlsx)。

### 対象の指定 (SearchFieldName / ListFieldName のどちらか一方だけを設定)

一括ダウンロードの対象データと、一括更新の対象モジュールは、画面に見えている検索結果/一覧から決まります。
次の 2 つのうち**どちらか一方だけ**を設定します (両方設定・両方未設定はデザインチェックエラー)。

| 条件ソース | 動作 |
|---|---|
| SearchFieldName | 同一モジュール内の検索フィールドを指定。**ユーザーが検索フィールドに入力した現在の検索条件**でダウンロードする |
| ListFieldName | 同一モジュール内のリストフィールド (List/DetailList/TileList) を指定。**そのリストの表示中の検索条件** (デザイン条件+検索条件+親フィールド参照の実値) でダウンロードする |

- ListFieldName 指定時、一覧レイアウトの列には絞られず**全列**、ページサイズ (SearchCondition.LimitCount) にも縛られず**全件**が出力されます
- アップロード (一括更新) は条件ソースから解決した**対象モジュール**へ取り込みます (条件自体は取込に影響しません)
- アップロード成功後、対象の一覧を自動で再読み込みします

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| SearchFieldName | string | △ | 条件ソース: 検索フィールド名 (ListFieldName とどちらか一方だけ設定) |
| ListFieldName | string | △ | 条件ソース: リストフィールド名 (SearchFieldName とどちらか一方だけ設定) |
| CanBulkDataDownload | bool | - | ダウンロードボタンを表示するか。既定 true |
| CanBulkDataUpdate | bool | - | アップロードボタンを表示するか。既定 true (権限の検証はサーバー側で行われる) |
| OnUploaded | string | - | 一括更新成功後に呼ばれるスクリプトイベント (一覧の再読み込みはイベントと無関係に自動で行われる) |

### モジュール JSON 例

親レコード (受注) の明細を、詳細ページ上のリストフィールドの条件でダウンロード/アップロードする例:

```json
{
  "ListFieldName": "OrderDetails",
  "Name": "DetailTransfer",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.BulkFileTransferButtonFieldDesign"
}
```

検索フィールドの現在の検索条件でダウンロードする例:

```json
{
  "SearchFieldName": "OrderSearch",
  "Name": "SearchResultTransfer",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.BulkFileTransferButtonFieldDesign"
}
```

### サーバー側の対応 (必須)

一覧ページの一括ダウンロード/一括更新と同じ API (`list_file` / `submit_by_file`) を使うため、
サーバーテンプレートの `ModuleDataController` が対応済みである必要があります
(テンプレートの実装例は `CsvFileFormatField` のドキュメントを参照)。

## Script

### スクリプト API

このフィールドはスクリプト API を公開していません。
スクリプトから一括ダウンロードを実行する場合は `BulkFileTransferService` を使います
(`BulkFileTransferService.Download(検索フィールド/リストフィールド/ModuleSearcher)`)。
アップロードはファイル選択を伴うため画面のボタンから実行します。

### イベント

| イベント | 説明 |
|---|---|
| OnUploaded | 一括更新が成功した後 (一覧の再読み込み後) に呼ばれる |

## CSS

### CSS カスタマイズ

一覧ページの一括ボタンと同じ見た目 (`btn btn-outline-secondary`、アイコンのみ) で描画されます。
ダウンロードボタンは `data-system="bulk-download"`、アップロードボタンは `data-system="bulk-upload"` 属性を持ちます。
