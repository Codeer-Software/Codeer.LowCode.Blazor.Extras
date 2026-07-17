一括ダウンロードをスクリプトから実行するサービス。
一覧ページや `BulkFileTransferButtonField` の一括ダウンロードと同じサーバー処理 (`list_file`) を使うため、
ファイル形式や列構成は対象モジュールの `CsvFileFormatField` / `FileColumnMappingField` の定義に従う
(どちらも未定義なら xlsx)。ファイル名は `{モジュール名}.{拡張子}`。

- `Download(ModuleSearcher)` … ModuleSearcher で組んだ条件でダウンロードする (Limit/Select も条件に従う)
- `Download(SearchField 検索フィールド)` … 検索フィールドの現在の検索条件でダウンロードする
- `Download(ListField リストフィールド)` … リストの表示中の検索条件でダウンロードする (一覧レイアウトの列・ページサイズには縛られず全列/全件)

このサービスを使うアプリはサーバー側の対応実装 (`BulkFileTransfer` への移譲) が必要。

```csharp
// 条件を組んでダウンロード
async void ExportOpenOrders_OnClick()
{
    var searcher = new ModuleSearcher("Order");
    searcher.AddEquals(e => e.Status.Value, "Open");
    await BulkFileTransferService.Download(searcher);
}

// 画面の検索フィールド/リストフィールドの条件でダウンロード
async void ExportSearchResult_OnClick()
{
    await BulkFileTransferService.Download(OrderSearch);   // SearchField
}

async void ExportList_OnClick()
{
    await BulkFileTransferService.Download(OrderList);     // ListField
}
```
