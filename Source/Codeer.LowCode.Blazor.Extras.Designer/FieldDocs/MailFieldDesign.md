# MailField (メール送信)

単発メール送信フィールド。**単発送信の唯一の入口**。
**レイアウトに置くと送信ボタン (アイコン) として表示され、押すと確認ダイアログの後に送信する** (結果はトーストで通知)。
置かずにスクリプトの Send() からだけ使うこともできる (前処理や独自の確認を挟むなら ButtonField + スクリプト)。
各項目は「**値**」と「**変数**」のペアで指定でき、**値が入っていれば値、空なら変数を自レコードで解決**する。
値はスクリプトからも設定できるため、宛先も文面も動的な送信もこのフィールドで行う。

使い分け:

- **MailField (このフィールド)**: 単発送信すべて (定型テンプレートも、スクリプトで組み立てる動的送信も)
- **BulkMailField**: 名簿リストへのサーバー解決一斉送信 (アドレスはクライアントに渡らない)

承認メンバーモジュールに置いて承認メンバー契約の `TurnNotifyMail` に指定すると、
承認の順番が回ってきたメンバーへの自動通知のテンプレートにもなる
(ApprovalMemberContractField のドキュメント参照)。

このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) と
appsettings の `Mail` 設定 (使うプロバイダのセクション) が必要。

## Design

### プロパティ (値と変数のペア。値が入っていれば値が優先)

| ペア | 説明 |
|---|---|
| 宛先変数 / 宛先 | 宛先アドレス。変数 = "Email.Value" (リンクパス可)、値 = 固定アドレス (カンマ / セミコロン区切りで複数可) |
| Cc変数 / Cc | Cc アドレス (宛先と同じ規則) |
| Bcc変数 / Bcc | Bcc アドレス (宛先と同じ規則) |
| 件名変数 / 件名 | 件名テンプレート。変数 = テンプレートを持つ自モジュールの変数、値 = テンプレート文字列。どちらの経路でも `{Title.Value}` のような {変数} は自レコードで解決される |
| 本文変数 / 本文 | 本文テンプレート (件名と同じ規則) |
| 返信先変数 / 返信先 | 返信先アドレス |

ペアでないもの:

| プロパティ | 説明 |
|---|---|
| HTML本文 | 本文を HTML として送るか。スクリプトから設定可 |

差出人は常に**送信インフラ設定のシステム送信者** (アドレス指定はできない = なりすましの構造的排除)。
| メールインフラ名 | 送信先の呼び名 (どの送信インフラで送るか)。呼び名を実際の送信インフラに対応づけるのはアプリのサーバー側 (MailController の対応表)。**省略可**で、省略 (空) なら appsettings の `Mail.DefaultInfraName` が使われる (どちらも空なら「呼び名未指定」の送信エラー)。**書かないのが基本形**。**デザイン固定** (スクリプトからは変更不可) |
| プレビューボタンを表示 | 送信ボタンの横に「プレビュー」を出す (既定 ON)。押すと差出人・宛先・解決後の件名/本文 (変数ハイライト付き) を自己完結 HTML でダウンロードする。解決は送信と同じ経路。サーバーの MailTransport.PreviewMailEndPoint が必要 |

- 宛先 (変数か値のどちらか) と、件名 / 本文のどちらかは必須 (デザインチェックが検証)
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

値 (To/Cc/Bcc/Subject/Body/ReplyTo/IsBodyHtml) は
スクリプトから設定でき、**設定した値は変数より優先**される (メールインフラ名はデザイン固定)。

```csharp
// デザイン宣言どおりに送る (ButtonField の OnClick から)
void SendReceiptMail()
{
    var result = ReceiptMail.Send();   // MailSendResult (IsSuccess / Failures)
    if (!result.IsSuccess) Toaster.Error("送信失敗: " + result.Failures[0].Error);
}

// 完全に動的な送信 (旧 Mail スクリプトオブジェクトの置き換え)
void SendDynamic()
{
    ReceiptMail.To = "sato@example.com;suzuki@example.com";
    ReceiptMail.Subject = "月次レポート";                    // 値もテンプレートとして {変数} が解決される
    ReceiptMail.Body = "今月のレポートを添付します。";
    ReceiptMail.AddTextAttachment("memo.txt", "テキスト添付");  // Excel は AddAttachment("report.xlsx", excel)
    var result = ReceiptMail.Send();                        // 添付は送信後にクリアされる
}

// 送らずに解決結果を HTML でダウンロード (プレビューボタンと同じ)
ReceiptMail.Preview();
```

送信は送信履歴 (appsettings の Mail.HistoryModuleName + MailHistoryContractField) にも記録される
(SourceModule / SourceId = このレコード)。失敗は戻り値に加えて Logger にも自動で記録される。

## CSS

レイアウトに置いたときは Bootstrap のボタングループ (`btn-group`、アイコンのみ) として描画される: 送信 (`btn btn-outline-primary`、送信アイコン。
押すと件名・宛先数の確認ダイアログを出してから送る) と、その右にプレビュー (ダウンロードアイコン。ShowPreviewButton=false で非表示)。
文字付きのボタンにしたい場合は ButtonField を置いてスクリプトから `Send()` / `Preview()` を呼ぶ。
`data-system="mail"` 属性を持つ。
