メールを送信する。Web アプリはサーバーのメールエンドポイント (`appsettings.json` の `MailSettings`)、デスクトップアプリはホスト側フック経由で送信される (どちらもテンプレートに設定済み)。

- 件名と本文だけの単純な送信は `SendEmail(address, subject, message)`
- 宛先複数・Cc/Bcc・HTML 本文・添付が必要なら `CreateMessage()` で `MailMessage` を組み立てて `Send(message)`

```csharp
// シンプルな送信
void SendNotification_OnClick()
{
    var success = MailService.SendEmail(Email.Value, "注文確認", "注文番号: " + OrderId.Value);
    if (success)
    {
        Toaster.Success("メールを送信しました");
    }
    else
    {
        Toaster.Error("メール送信に失敗しました");
    }
}

// 宛先複数・Cc・Excel 添付
void SendReport_OnClick()
{
    var searchFile = new ModuleSearcher<TestFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Template");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        excel.OverWrite(this);

        var msg = MailService.CreateMessage();
        msg.AddTo("sato@example.com;suzuki@example.com");
        msg.AddCc("manager@example.com");
        msg.SetSubject("月次レポート");
        msg.SetBody("今月のレポートを添付します。");
        msg.AddAttachment("report.xlsx", excel);

        if (MailService.Send(msg))
        {
            Toaster.Success("メールを送信しました");
        }
    }
}
```
