# MailSenderContractField (差出人契約)

**操作ユーザー（＝差出人）のモジュールが「メールアドレスと表示名はどの値か」を宣言する契約フィールド。**
UI もデータも持たない設定運搬フィールドで、**デザインの「現在のユーザーのモジュール」**（AppUser 等）に1つ置く。

これを使うのは:

- **「自分を差出人にする」(IsFromCurrentUser)** — MailField / BulkMailField。サーバーが認証ユーザーIdでこのモジュールを引き、宣言されたアドレス・表示名を差出人にする
- **GmailTokenField のユーザー単位トークン検索** — 差出人アドレスで人を引くのに使う

差出人の解決はプロバイダ非依存の共通層（`MailDispatcher`）が行うので、**この契約もプロバイダに依存しません**
（送れるかどうかはプロバイダの能力差。`IMailSender` は解決済みの From を見るだけ）。

## セットアップ

手で置かなくても**メールのセットアップ** (デザイナ Tools > メールのセットアップ / CLI `mail-setup`) が
ユーザーモジュールに追加する (メールアドレス・表示名のフィールド名を指定。既にあれば触らない)。
承認フローのセットアップで「メールを使う」を選んだ場合も同じ。

## Design

| プロパティ (表示名) | 型 | 必須 | 説明 |
|---|---|---|---|
| Name (名前) | string | ○ | フィールド名 |
| Email (メールアドレス (必須)) | string | ○ | アドレスの変数。自モジュールのフィールド (`Email.Value`) かリンクパス (`Employee.Email.Value`) |
| DisplayName (表示名) | string | - | 差出人の表示名の変数。**空 = 使わない**（表示名なし） |

- 必須の役割はデザイナの表示名に「(必須)」が付く。必須以外は空にすれば「使わない」宣言になり、デザインチェックのエラーにもならない
- リンク先のフィールドを改名しても追従する
- 同じモジュールに2つ置くとエラー

### JSON 例

```json
{
  "Name": "MailSender",
  "Email": "Email.Value",
  "DisplayName": "Name.Value",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MailSenderContractFieldDesign"
}
```

## デザインチェック

- **「自分を差出人にする」が ON の MailField / BulkMailField があるのに、現在のユーザーのモジュールにこの契約が無ければエラー**
- **GmailTokenField を置いたモジュール（＝現在のユーザーのモジュール）にこの契約が無ければエラー**（アドレスで引けないため）
- 役割の変数が解決できない場合もエラー

## 一斉送信の宛先契約との違い

| | 置く先 | 役割 | 使う機能 |
|---|---|---|---|
| **差出人契約** (このフィールド) | 現在のユーザーのモジュール | Email / DisplayName | 自分を差出人にする・Gmailトークン検索 |
| **一斉送信の宛先契約** (BulkMailRecipientContractField) | 一斉送信の宛先リストが指すモジュール | Email / OptOut | 一斉送信の宛先解決 |

## Script

スクリプト API は無い（設定運搬フィールド）。

## CSS

描画しない。
