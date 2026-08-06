一括ダウンロードと一括保存をスクリプトから実行するサービス。
ダウンロードは一覧ページや `BulkFileTransferButtonField` の一括ダウンロードと同じサーバー処理を使うため、
ファイル形式や列構成は対象モジュールの `CsvFileFormatField` / `FileColumnMappingField` の定義に従う
(どちらも未定義なら xlsx)。ファイル名は `{モジュール名}.{拡張子}`。

- `Download(ModuleSearcher)` … ModuleSearcher で組んだ条件でダウンロードする (Limit/Select も条件に従う)
- `Download(SearchField 検索フィールド)` … 検索フィールドの現在の検索条件でダウンロードする
- `Download(ListField リストフィールド)` … リストの表示中の検索条件でダウンロードする (一覧レイアウトの列・ページサイズには縛られず全列/全件)
- `Download(List<モジュール> 一覧)` … 加工済みのモジュール列をそのままファイル化する (検索しない。サーバー処理は `list_file_by_data`)。
  スクリプトで行を変換・絞り込み・組み立てしてから出力する用途
- `Submit(List<モジュール> 一覧)` … 加工済みのモジュール列を一括保存する (1トランザクション。サーバー処理は `bulk_submit`)。
  Id の一致で追加/更新を判定し (Id なし・テンポラリ Id は新規)、新規行がまとまっていれば multi-row INSERT で高速に挿入される。
  戻り値は `bool` (true=保存成功)。**保存した新規行の採番 Id (テンポラリ Id の解決) は返らない**ため、
  保存後もモジュールを使い続ける (編集して再保存する) 用途には `Module.Submit` を使う。取込のような投げ切り保存用

条件系 3 つは「サーバーで検索した結果をそのまま出力」、`List<モジュール>` 版だけが「クライアントのデータを出力」。
スクリプトで行に手を加える必要が無ければ条件系を使う方が速くて簡単。

ファイル形式・列対応の定義はスクリプト変換と常に併用する。排他なのは「同じ列」への二重のコード変換だけ
(宣言的な `ConversionModule` とスクリプト変換は列ごとにどちらか一方。スクリプトで変換済みの値に
さらに宣言的変換はかけない)。

このサービスを使うアプリはサーバー側の対応実装が必要。`Download(List<モジュール>)` / `Submit(List<モジュール>)` のエンドポイント URL はアプリの初期化 (ServiceInitializer の `BulkFileTransferService.ListFileByDataEndPoint` / `BulkSubmitEndPoint`) で設定する (テンプレートは設定済み)。

一括取込 (ファイル → 加工 → `Submit`) の全体像とパターン集は `BulkFileReader` を参照。

```csharp
// 条件を組んでダウンロード (行の加工が不要ならこれが基本形)
void ExportOpenOrders_OnClick()
{
    var searcher = new ModuleSearcher<注文>();
    searcher.AddEquals(e => e.Status.Value, "Open");
    BulkFileTransferService.Download(searcher);
}

// 画面の検索フィールド/リストフィールドの条件でダウンロード
void ExportSearchResult_OnClick()
{
    BulkFileTransferService.Download(OrderSearch);   // SearchField
}

void ExportList_OnClick()
{
    BulkFileTransferService.Download(OrderList);     // ListField
}
```

## スクリプトで行を加工してから出力する (取得 → 変換 → 出力)

`ModuleSearcher.Execute()` で取得し、行を加工して `Download(List<モジュール>)` に渡す。
変換表・マスタは行ループの外で一度だけ辞書化する (行ループ内で ModuleSearcher を使わない)。

```csharp
void ExportConverted_OnClick()
{
    var searcher = new ModuleSearcher<注文>();
    var list = searcher.Execute();

    // コード変換 (内部 → 相手仕様)。表引きで済むなら列マッピングの ConversionModule に任せてもよい
    var mapSearcher = new ModuleSearcher<コード変換表>();
    var maps = mapSearcher.Execute();
    var dic = new Dictionary<string, string>();
    foreach (var mp in maps) { dic[mp.内部コード.Value] = mp.外部コード.Value; }

    foreach (var m in list)
    {
        if (dic.ContainsKey(m.コード.Value)) { m.コード.Value = dic[m.コード.Value]; }
        // 計算列・結合列は出力用フィールドに値を作って列マッピングでその列を出す
        m.表示名.Value = m.コード.Value + ":" + m.品名.Value;
    }
    BulkFileTransferService.Download(list);
}

// 行の絞り込み・並び替えをしてから出力
void ExportFiltered_OnClick()
{
    var searcher = new ModuleSearcher<注文>();
    var list = searcher.Execute();
    var output = new List<注文>();
    foreach (var m in list)
    {
        if (m.数量.Value > 0) { output.Add(m); }
    }
    BulkFileTransferService.Download(output);
}
```

出力される列・並び・外部列名・書式・固定長の桁は `FileColumnMappingField` (無ければ内部名ヘッダ) が決める。
スクリプトの役割は「行の値を作ること」で、列構成そのものはデザイン側の関心事。

取込側 (ファイル → `List<モジュール>` → 加工 → 書き込み) は `BulkFileReader` を参照。
