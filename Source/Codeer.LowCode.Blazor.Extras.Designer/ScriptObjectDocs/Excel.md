Excel ファイルの読み書きとダウンロード。`IDisposable` のため `using` で使用する。
FileField から取得したテンプレート Excel に値を書き込み、xlsx または PDF でダウンロードする用途が典型。

- `OverWrite(Module data)` はテンプレート内の `{{フィールド名}}` プレースホルダをモジュールの値で置換する
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
