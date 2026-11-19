Job スクリプト (Jobs/名前.job.cs) からメールを送る (画面のスクリプトには無い)。`MailMessage` を組み立てて `MailService.Send(message)` で送る。

- 戻り値は `MailSendResult` (`IsSuccess` / `TotalCount` / `SuccessCount` / `Failures` (`To` と `Error`))。送れなくても例外にならないので `IsSuccess` を見る (送信履歴を書けなかったときは例外 = Job は失敗する)
- 送信インフラ・誤送信防止 (DebugRedirectAllTo)・送信履歴はサーバーのメールの設定 (画面のメールと同じ) に従う。差出人は送信インフラ設定のシステム送信者 (`From` を入れても使われない)
- **送ったメールは取り消せない**。Job が後で失敗する (`Fail`・実行時エラー) と書き込みと送信履歴は取り消されるが、メールは戻らない。データを書く Job は `DBService.Commit()` で確定してから送る (送った後にも `DBService.Commit()` を呼ぶと送信履歴が残る)
- 添付は `MailAttachment` (`FileName` / `ContentBase64`)。Excel は `excel.ToAttachment(fileName)` / `excel.ToPdfAttachment(fileName)` で作れる

```csharp
string Run(JobArguments args)
{
    var search = new ModuleSearcher<TaskItem>();
    search.AddEquals(e => e.Status.Value, "Open");
    var rows = search.ExecuteRaw();
    foreach (var row in rows)
    {
        var mail = new MailMessage();
        mail.To.Add(row.AssigneeEmail.Value);
        mail.Subject = "期限が近いタスク: " + row.Title.Value;
        mail.Body = row.Title.Value + " の期限が近づいています。";
        var result = MailService.Send(mail);
        if (!result.IsSuccess) Logger.Error(result.Failures[0].Error);
    }
    return $"{rows.Count} 件";
}
```
