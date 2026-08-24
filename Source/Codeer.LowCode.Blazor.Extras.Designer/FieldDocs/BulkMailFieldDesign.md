## Design

詳細レイアウトに配置できる一斉メール送信ボタンです (Salesforce のキャンペーン + リストメールと同型)。
同一モジュール上のリストフィールド (List/DetailList/TileList) を宛先リストとして参照し、
そのリストの検索条件に合致する全行 (画面のページングとは無関係) へ一斉送信します。
件名・本文は自モジュールのフィールドをテンプレートとして参照できるため、
配信レコードごとに文面を変えられます。

### 考え方 (Salesforce と同じ)

- **宛先リストの行 = 送る対象そのもの**。「今回はこの人を外す」は行の削除 (または最初から入れない) で行う
- **配信停止 (オプトアウト) は人の恒久属性**。宛先の人 (リンク先) の Boolean フィールドを
  `OptOutVariable` で指しておくと、送信時に常にスキップされる (最終安全弁)。
  Salesforce の Email Opt Out (`HasOptedOutOfEmail`) に相当

### 動作

1. 送信ボタン押下 → 対象件数の確認ダイアログを表示
2. サーバーが宛先リストの検索条件から宛先を解決して送信
   (アドレスはクライアントに渡らない。読み取り権限・行条件が効く。上限は appsettings の MaxBulkCount)
3. `OptOutVariable` が true の行と、宛先アドレスが空の行はスキップ
4. `DbColumn` を設定していれば、送信結果サマリ (JSON) がこのレコードの列に書き戻される
   (送信履歴と同じサーバー内部経路。操作ユーザーの書き込み権限に依存しない)

未保存の変更があるレコードからは送信できません (保存済みの状態=サーバーから見える状態が送信対象)。
新規レコード (未保存) も同様です。

### テンプレートの変数

件名・本文の `{変数}` は宛先の行で解決されます。デザインの変数と同じ表記です。

- `{Name.Value}` … 宛先行のフィールドの値 (数値・日付はフィールドの書式で整形)
- `{Rank}` / `{Rank.DisplayText}` … Select/Link は表示テキスト。`{Rank.Value}` はコード値
- `{Contact.Email.Value}` … リンクパス (Link/SelectField の参照先モジュールのフィールド)
- リテラルの `{` `}` は `{{` `}}`
- レコードへのリンクを入れたい場合は URL を直書きして id 変数を混ぜる
  (例: `https://app.example.com/Main/Contact/{Contact.Id.Value}`)

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| RecipientListFieldName | string | ○ | 宛先リストのフィールド名 (List/DetailList/TileList) |
| EmailAddressVariable | string | ○ | 宛先モジュールの、メールアドレスを持つ変数。リンクパス可 (`Contact.Email.Value`) |
| OptOutVariable | string | - | 配信停止 (Boolean) の変数。人側をリンクパスで指すのが典型 (`Contact.メール拒否.Value`) |
| SubjectVariable | string | - | 件名テンプレートを持つ自モジュールの変数 (`Title.Value`)。Subject (値) が入っている場合はそちらが優先 |
| Subject | string | - | 件名テンプレート (固定文字列) |
| BodyVariable | string | - | 本文テンプレートを持つ自モジュールの変数 (`Body.Value`)。Body (値) が入っている場合はそちらが優先 |
| Body | string | - | 本文テンプレート (固定文字列・複数行) |
| MailInfraName | string | - | 送信先の呼び名 (どの送信インフラで送るか。対応づけはアプリの MailController の対応表)。**省略可**で、省略 (空) なら appsettings の `Mail.DefaultBulkInfraName` → `DefaultInfraName`、それも空ならアプリの既定。「一斉は配信サービス、単発は通知系」の対を appsettings 側で決めておき、フィールドには書かないのが基本形 |
| IsBodyHtml | bool | - | 本文を HTML として送るか (変数値は HTML エスケープされる) |
| IsFromCurrentUser | bool | - | ON = 操作ユーザー本人のアドレスが差出人 (サーバーが解決)。OFF = 送信インフラ設定の差出人。**差出人のアドレス指定はできない** (なりすましの構造的排除)。要: デザインの CurrentUser モジュール設定と `Mail.UserEmailFieldName` (既定 "Email") |
| ReplyToVariable | string | - | 返信先アドレスの変数 (自モジュールの変数・リンクパス可)。ReplyTo (値) が入っている場合はそちらが優先 |
| ReplyTo | string | - | 返信先アドレス (値) |
| ButtonText | string | - | ボタンの表示テキスト。空なら既定の文言 |
| DbColumn | string | - | 送信結果サマリ (JSON) の保存列。空ならサマリを保存しない |

件名・本文などの「変数 / 値」ペアは**値が入っていれば値、空なら変数**です (MailField と同じ規則)。
どちらも空のままだとデザインチェックが知らせます。

### 典型構成 (キャンペーン + 名簿 = Salesforce の Campaign + CampaignMember)

```
メール配信 (MailCampaign)          … 1レコード = 1配信
├── Title (Text)                  … 件名テンプレート
├── Body (Text 複数行)             … 本文テンプレート
├── Members (DetailListField → 配信対象)   … 送る対象そのもの
└── BulkMail1 (BulkMailField)
      RecipientListFieldName: Members
      EmailAddressVariable: Contact.Email.Value
      OptOutVariable: Contact.メール拒否.Value   … 人側の配信停止(最終安全弁)
      SubjectVariable: Title.Value / BodyVariable: Body.Value / DbColumn: send_summary

配信対象 (CampaignMember)
├── Campaign (Link → メール配信)
└── Contact (Link → 担当者)        … 精査 = この行を足す/消す

担当者 (Contact)
├── Email (Text)
└── メール拒否 (Boolean)           … 配信停止。キャンペーンに関係なく常に尊重される
```

対象の取込 (条件検索→名簿へ一括生成) はスクリプトのボタンや CSV 取込で行います。
名簿を持たず条件で直接送る場合は RecipientListFieldName に ListField (検索条件に自モジュールの
フィールドをバインド) を指定します。画面の一覧＝送信対象のプレビューになります。

### 送信結果サマリ (DbColumn)

列には `BulkMailSummaryEntry` の JSON 配列 (新しい順・最大20件) が入ります。
1件 = `SentAt / Sender / Subject / TotalCount / SuccessCount / FailureCount / Failures(先頭5件)`。
ボタンの下に最終送信の結果が表示されます。全送信の監査記録が必要な場合は appsettings の
`Mail.HistoryModuleName` (履歴モジュール) を併用します。

### 前提

このフィールドを使うアプリはサーバー側のメール送信対応 (MailController と appsettings の Mail 設定) が必要です。

## Script

### スクリプト API

- `Send()` … 送信を実行し `MailSendResult` を返す。確認ダイアログ・トーストは出さない
  (呼び出し側で MessageBox 等を使って制御する)。未保存の変更があると失敗を返す
- `Value` … 送信結果サマリの JSON 文字列 (読み取り)

一斉送信の入口はこのフィールドに一本化されています。単発送信 (個別の宛先へスクリプトから送る)
は MailField を使います (値プロパティをスクリプトから設定すれば動的送信も可能)。

## CSS

### CSS カスタマイズ

Bootstrap のボタン (`btn btn-outline-primary`、封筒アイコン + テキスト) として描画されます。
`data-system="bulk-mail"` 属性を持ちます。最終送信結果は `form-text` で表示されます。
