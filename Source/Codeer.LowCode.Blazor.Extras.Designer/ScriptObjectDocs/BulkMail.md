一斉送信。`new BulkMail()` で作り、テンプレート ({フィールド名} 差し込み) と宛先ソースを設定して `Send()`。
宛先ソースは **Rows / Searcher / AddRecipient のどれか1つだけ** 設定する (複数設定すると失敗が返る)。

| プロパティ | 説明 |
|---|---|
| `Sender` | 使うセンダー名 (`Mail.Senders` の Name)。省略時は先頭 |
| `Subject` / `Body` | 件名・本文テンプレート。`{フィールド名}` が宛先ごとの値で差し込まれる (`{{` `}}` は `{` `}` のリテラル) |
| `IsBodyHtml` | HTML本文か。HTML時は差し込み値が自動エスケープされる |
| `ReplyTo` | 返信先 |
| `ToField` | 宛先行のメールアドレスフィールド名 (Rows/Searcher のとき必須) |
| `ExcludeField` | 宛先行の除外フラグ (Boolean)。true の行には送らない (配信停止) |
| `Rows` | 宛先ソース①: 行リスト (一覧の Rows や検索結果) |
| `Searcher` | 宛先ソース②: ModuleSearcher。**宛先はサーバーで解決** (宛先一覧が画面に載らない・大量送信向け)。この経路では `{RecordUrl}` (レコード詳細へのリンク。appsettings の `Mail.AppBaseUrl` が必要) が使える |
| `Source` | 送信履歴に記録する元レコード (Module。省略可) |

| メソッド | 説明 |
|---|---|
| `AddRecipient(address)` | 宛先ソース③: 宛先を自分で組み立てる (`MailRecipient` を返す) |
| `AddAttachment(fileName, excel)` / `AddTextAttachment(fileName, text)` | 添付 (全宛先共通) |
| `Preview(row)` / `PreviewSubject(row)` | 行の値で差し込んだ本文/件名を返す (送信前確認) |
| `Send()` | 送信。`MailSendResult` (TotalCount / SuccessCount / Failures) を返す |

```csharp
// 一覧の行に一斉送信
void SendCampaign_OnClick()
{
    var bulk = new BulkMail();
    bulk.Sender = "Campaign";
    bulk.Subject = "{Name} 様へのご案内";
    bulk.Body = "いつもありがとうございます。{Name} 様の担当は {SalesName} です。";
    bulk.ToField = "Email";
    bulk.ExcludeField = "OptOut";
    bulk.Rows = CustomerList.Rows;
    bulk.Source = this;

    var result = bulk.Send();
    Toaster.Success(result.SuccessCount + "/" + result.TotalCount + " 件送信しました");
}

// 検索条件で一斉送信(サーバー解決・大量向け)
void SendToGoldMembers_OnClick()
{
    var searcher = new ModuleSearcher<Customer>();
    searcher.AddEquals(e => e.Rank.Value, "Gold");

    var bulk = new BulkMail();
    bulk.Sender = "Campaign";
    bulk.Subject = "ゴールド会員限定のご案内";
    bulk.Body = "詳細はこちら: {RecordUrl}";
    bulk.ToField = "Email";
    bulk.ExcludeField = "OptOut";
    bulk.Searcher = searcher;
    bulk.Send();
}
```

- 件数がセンダーの `MaxBulkCount` (既定10000) を超えるとエラー。大量送信は SendGrid 系のセンダーを使うこと
  (SMTP は逐次送信、Graph は Exchange Online のレート制限があり一斉送信に不向き)
- 失敗した宛先は `result.Failures` (To / Error) に入る。履歴モジュール設定時は失敗明細もJSONで記録される
