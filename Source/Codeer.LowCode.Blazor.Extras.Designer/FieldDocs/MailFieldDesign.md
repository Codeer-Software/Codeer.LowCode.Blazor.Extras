# MailField (メール送信)

単発メール送信フィールド (UI を持たない設定運搬 + スクリプト API)。
宛先・件名・本文テンプレートをデザインで宣言し、スクリプトの `Send()` が自レコードの値で
テンプレートの `{変数}` (リンクパス可) を解決して送信する。ボタンは ButtonField + スクリプトで置く。

使い分け:

- **MailField (このフィールド)**: このレコードに紐づく単発送信 (受付通知・担当者への連絡等)
- **BulkMailField**: 名簿リストへのサーバー解決一斉送信 (アドレスはクライアントに渡らない)
- **Mail スクリプトオブジェクト**: 宛先も文面も完全に動的な送信 (デザイン宣言の恩恵はない)

承認メンバーモジュールに置いて承認メンバー契約の `TurnNotifyMail` に指定すると、
承認の順番が回ってきたメンバーへの自動通知のテンプレートにもなる
(ApprovalMemberContractField のドキュメント参照)。

このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) と
appsettings の `Mail.Infras` 設定が必要。

## Design

### プロパティ

| プロパティ | 説明 |
|---|---|
| 宛先変数 | 宛先アドレスの変数 ("Email.Value"。リンクパス可)。空なら「宛先」の固定アドレス |
| 宛先 | 固定アドレス (カンマ / セミコロン区切りで複数可) |
| Cc変数 / Cc | Cc アドレス (宛先と同じ規則) |
| 件名変数 | 件名テンプレートを持つ自モジュールの変数。空なら「件名」の固定文字列 |
| 件名 | 件名テンプレート (固定)。`{Title.Value}` のような変数は自レコードで解決される |
| 本文変数 / 本文 | 本文テンプレート (件名と同じ規則) |
| メールインフラ名 | appsettings の Mail.Infras の設定名 (どの送信インフラ・既定差出人を使うか)。空なら既定 (Mail.DefaultInfraName → 先頭)。差出人アドレスを変えるのは「差出人変数」の方 |
| HTML本文 | 本文を HTML として送るか |
| 返信先 | 返信先アドレス |
| 差出人変数 | 差出人アドレスの変数 (任意・リンクパス可)。空なら送信インフラ設定の差出人。**サーバー設定 (Mail.Infras の AllowedFromDomains) で許可されたドメインのみ** (SFA の「担当者から送る」用) |
| 差出人表示名変数 | 差出人表示名の変数 (差出人変数の指定時のみ使われる) |

- 宛先 (変数か固定のどちらか) と、件名 / 本文のどちらかは必須 (デザインチェックが検証)
- 変数はデザインチェックで存在検証され、フィールドのリネームに追従する
- テンプレートの `{変数}` のリンク先の値をクライアントで解決する場合は、
  そのパスがロードされていること (DataOnlyFields 等。宣言済みドット列と同じ規則)

### JSON 例

```json
{
  "Name": "ReceiptMail",
  "ToVariable": "Email.Value",
  "Subject": "経費申請: {Title.Value}",
  "Body": "{Title.Value} (金額 {Amount.Value}) を受け付けました。",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MailFieldDesign"
}
```

## Script

```csharp
// ButtonField の OnClick から
void SendReceiptMail()
{
    var result = ReceiptMail.Send();   // MailSendResult
    if (!result.IsSuccess) Logger.Error("メール送信に失敗しました");
}
```

送信は送信履歴 (appsettings の Mail.HistoryModuleName + MailHistoryContractField) にも記録される
(SourceModule / SourceId = このレコード)。
