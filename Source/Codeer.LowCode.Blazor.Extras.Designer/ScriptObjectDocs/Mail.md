メールを1通送る。`new Mail()` で作り、プロパティを設定して `Send()`。
単発でもこのレコードに紐づく定型送信なら MailField (デザイン宣言+チェック+リネーム追従が効く) を推奨。Mail オブジェクトは宛先も文面も完全に動的な送信用。
送信インフラは `appsettings.json` の `Mail.Infras` (SMTP / Microsoft Graph / SendGrid / Gmail を名前付きで定義。旧 `MailSettings` も既定インフラとして有効)。
複数のインフラを併用するシステムでは `Mail.DefaultInfraName` (単発の既定) / `Mail.DefaultBulkInfraName` (一斉の既定) を設定し、送信箇所ではインフラ名を書かないのが基本 (例外の送信箇所だけ明示)。

| プロパティ | 説明 |
|---|---|
| `Sender` | 使うインフラ名 (`Mail.Infras` の Name)。省略時は `Mail.DefaultInfraName`、無ければ先頭 |
| `Subject` / `Body` | 件名・本文 |
| `IsBodyHtml` | HTML本文か (既定 false) |
| `ReplyTo` | 返信先 |
| `Source` | 送信履歴に記録する元レコード (Module。省略可) |

| メソッド | 説明 |
|---|---|
| `AddTo(address)` / `AddCc(address)` / `AddBcc(address)` | 宛先を追加 (`;` 区切りで複数可) |
| `AddAttachment(fileName, excel)` / `AddTextAttachment(fileName, text)` | 添付 |
| `Send()` | 送信。`MailSendResult` (IsSuccess / Failures) を返す |

```csharp
void SendNotification_OnClick()
{
    var mail = new Mail();
    mail.MailInfraName = "Notify";           //省略可(既定インフラ。Mail.Infras の設定名 = 送信インフラの選択)
    mail.AddTo(CustomerEmail.Value);
    mail.Subject = "注文確認";
    mail.Body = "注文番号: " + OrderId.Value;
    mail.Source = this;                    //送信履歴にこのレコードを記録

    var result = mail.Send();
    if (result.IsSuccess) Toaster.Success("メールを送信しました");
    else Toaster.Error("メール送信に失敗しました: " + result.Failures[0].Error);
}

// Excel帳票を添付して送る
void SendReport_OnClick()
{
    var searchFile = new ModuleSearcher<TestFiles>();
    searchFile.AddEquals(e => e.Name.Value, "Template");
    var file = searchFile.Execute()[0];

    using (var memory = file.File.GetMemoryStream())
    using (var excel = new Excel(memory, file.File.FileName))
    {
        excel.OverWrite(this);

        var mail = new Mail();
        mail.AddTo("sato@example.com;suzuki@example.com");
        mail.AddCc("manager@example.com");
        mail.Subject = "月次レポート";
        mail.Body = "今月のレポートを添付します。";
        mail.AddAttachment("report.xlsx", excel);
        mail.Send();
    }
}
```

送信履歴: `appsettings.json` の `Mail.HistoryModuleName` に履歴モジュールを設定すると、送信1回につき1レコードが自動で記録される
(予約名フィールド `SentAt` / `MailInfraName` / `Subject` / `TotalCount` / `SuccessCount` / `FailureDetails` / `SourceModule` / `SourceId` のうち置いたものだけ書かれる)。
