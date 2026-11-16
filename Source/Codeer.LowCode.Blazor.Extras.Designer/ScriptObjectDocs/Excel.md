Excel ファイルの読み書きとダウンロード。`IDisposable` のため `using` で使用する。
FileField から取得したテンプレート Excel に値を書き込み、xlsx または PDF でダウンロードする用途が典型。

- `OverWrite(Module data)` はテンプレートの `$` で始まるセル (`$フィールド名.Value` など、`this` から辿れるメンバーの名前) をモジュールの値で置換する。`#LoopRow($一覧.Rows, row)` の行は一覧の行ごとに繰り返す (記法は Excel.Report.PDF)
- `DownloadPdf()` の PDF 変換は、Web アプリではサーバーの変換エンドポイント、デスクトップではホスト側フック経由 (どちらもテンプレートに設定済み)

```csharp
// テンプレート Excel に書き出してダウンロード
void ExportExcel_OnClick()
{
    var searchFile = new ModuleSearcher<TestFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Template");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        excel.OverWrite(this);
        excel.Download();          // xlsx。PDF なら excel.DownloadPdf();
    }
}

// セルを個別に操作
void CustomExport_OnClick()
{
    var searchFile = new ModuleSearcher<TestFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Template");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        var cell = excel.FindCellByText("{{Name}}");
        if (cell != null)
        {
            excel.SetCellValue(cell, Name.Value);
        }
        excel.Download();
    }
}
```

## Job スクリプト (Jobs/名前.job.cs) での使い方

Job では Module が無いので、検索で読んだデータ (`ModuleData`) で埋めて、メールの添付にする (Job ではダウンロードできない = `Download()` / `DownloadPdf()` は使えない)。

- `OverWrite(ModuleData data)`: `OverWrite(Module)` と同じ記法。解決するのはデータに載っている項目と FieldData のプロパティだけ (モジュールのスクリプトの変数・関数、`$Resources.…` などサービスで始まる記号、画面のフィールドだけが持つメンバーは記号のまま残る)。デザインにあってデータに載っていない項目は空。`$一覧.Rows` は一覧の行、`$子.ChildModule.項目.Value` は ModuleField の子
- 一覧 (明細) の行は、検索の `Select` に一覧と `Id` を入れたときだけ親と一緒に読まれる
- `ToAttachment(fileName)` / `ToPdfAttachment(fileName)`: `MailAttachment` を返す。`MailService.Send` の `MailMessage.Attachments` に足す

```csharp
string Run(JobArguments args)
{
    var search = new ModuleSearcher<Order>();
    search.AddEquals(e => e.Id.Value, args.Options[0]);
    search.Select(e => e.Id, e => e.Title, e => e.Customer, e => e.Lines);
    var order = search.ExecuteRawFirstOrDefault();
    if (order == null) Fail("注文がありません");

    var mail = new MailMessage();
    mail.To.Add(CurrentUser.Email.Value);
    mail.Subject = "注文書 " + order.Title.Value;
    using (var excel = new Excel(Resources.GetMemoryStream("Reports/Order.xlsx"), "Order.xlsx"))
    {
        excel.OverWrite(order);
        mail.Attachments.Add(excel.ToPdfAttachment("注文書.pdf"));
    }
    var result = MailService.Send(mail);
    if (!result.IsSuccess) Fail(result.Failures[0].Error);
    return "送りました";
}
```
