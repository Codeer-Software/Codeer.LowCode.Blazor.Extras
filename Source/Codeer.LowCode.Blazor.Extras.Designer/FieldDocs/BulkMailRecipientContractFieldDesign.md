# BulkMailRecipientContractField (一斉送信の宛先契約)

**一斉送信の宛先モジュールが「メールの宛先として何を使うか」を宣言する契約フィールド。**
UI もデータも持たない設定運搬フィールドで、宛先モジュールに1つ置く。

**使うのは一斉送信 (BulkMailField) だけ**で、単発送信や差出人の解決には関与しない
(「自分を差出人にする」の操作ユーザーのアドレスは別の契約 = MailSenderContractField)。
BulkMailField は「宛先リストの先のモジュール」にこの契約が無いとデザインチェックでエラーになる。

## Design

| プロパティ (表示名) | 型 | 必須 | 説明 |
|---|---|---|---|
| Name (名前) | string | ○ | フィールド名 |
| Email (メールアドレス (必須)) | string | ○ | メールアドレスの変数。自モジュールのフィールド (`Email.Value`) か**リンクパス** (`Contact.Email.Value`) |
| DisplayName (表示名 (プレビューの一覧用)) | string | - | 宛先の表示名の変数 (`Name.Value` / リンクパス `Contact.Name.Value`)。プレビューの宛先一覧で人を見分けるためだけに使う。空 = アドレスだけ |
| OptOut (配信停止 (オプトアウト)) | string | - | 配信停止の Boolean 変数。true の宛先には送らない (最終安全弁)。**空 = 使わない** (判定なし) |

**必須の役割はデザイナの表示名に「(必須)」が付く**。必須以外は空にすればその項目を使わない宣言になり、
デザインチェックのエラーにもならない。

- 役割の値は**変数**なので、中間テーブル形式の名簿 (行 = 「キャンペーン × 人」) でも
  リンク先の人のアドレスを指せる。リンク先のフィールドを改名しても追従する
- 配信停止は**人の恒久属性**を指すのが典型 (退会・メール拒否)。
  「今回の配信の対象から外す」は名簿の行を削除するのが正道で、このフラグの用途ではない
- 同じモジュールに2つ置くとエラー (解決が曖昧になる)

### JSON 例

中間テーブル形式の名簿 (`CampaignMember` が `Contact` をリンクしている):

```json
{
  "Name": "MailRecipient",
  "Email": "Contact.Email.Value",
  "OptOut": "Contact.OptOut.Value",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.BulkMailRecipientContractFieldDesign"
}
```

名簿の行がアドレスを自分で持つ場合:

```json
{
  "Name": "MailRecipient",
  "Email": "Email.Value",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.BulkMailRecipientContractFieldDesign"
}
```

## どこに置くか

**BulkMailField の宛先リストが指しているモジュール**に1つだけ。
差出人 (「自分を差出人にする」) や Gmail のユーザー単位トークンのアドレスはこの契約とは無関係で、
**現在のユーザーのモジュールに置く差出人契約 (MailSenderContractField)** が宣言する。

## Script

スクリプト API は無い (設定運搬フィールド)。

## CSS

描画しない。
