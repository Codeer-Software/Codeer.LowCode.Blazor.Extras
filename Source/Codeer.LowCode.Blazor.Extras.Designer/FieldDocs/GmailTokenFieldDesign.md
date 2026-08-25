# GmailTokenField (Gmailトークン)

Gmail の**ユーザー単位のリフレッシュトークン**を保存するフィールド。
ユーザーモジュール (AppUser 等) に置くと、そのユーザーが MailField の「自分を差出人にする」で送ったメールが
**本人の Gmail から送られる** (送信済みも本人に残る)。

Gmail 固有の機能です。ドメイン全体の委任が特権管理者しか設定できないため、
「管理者権限なしで本人名義に送る」には本人の OAuth 同意で得たトークンが必要、という Gmail の事情に対応するもの。
他のインフラは上位のレイヤで解決するので、このフィールドは不要です
(GraphApi = アプリケーション権限でテナント内の任意ユーザーとして送れる /
SendGrid = ドメイン認証 / Smtp = リレー)。

## 仕組み

- トークンは**書き込み専用の DB 列**に保存され、**クライアントには一切返さない**
  (トークンは「所持 = そのユーザーとして送信できる」秘密のため)
- 列は**AES-GCM で暗号化**されて保存される。鍵は appsettings の `Gmail.TokenEncryptionKey`。
  **鍵が未設定のまま保存しようとするとエラー**になる (平文で保存しない)
- 入力欄はこのフィールド自身が持つ (**貼り付け**、または**トークンJSONファイルの読み込み**)。
  現在の値は読み出せないので**入力欄は毎回空から始まり、空のまま保存すれば既存トークンを維持**する
  (パスワード変更欄と同じ挙動)。「登録を解除」で登録を消せる
- 保存時の暗号化はサーバー側ヘルパが行う:
  `CustomizedModuleDataIO.AddAsync / UpdateAsync` で
  `Codeer.LowCode.Blazor.Extras.Server.Mail.GmailTokenHelper.ProtectGmailTokens(moduleDesign, data, SystemConfig.Instance.Gmail)` を
  `base.AddAsync / base.UpdateAsync` の前に呼ぶこと (PasswordHashHelper.ApplyPasswordHash と並べて呼ぶのが定石)
- 送信時の読み取り・復号はサーバー側の SQL のみ (`GmailUserTokenStore`。テーブル名・列名はデザインから、実行はテンプレートの DbAccess。MailController が結線)。
  **このフィールドが CurrentUser モジュールに無ければユーザー単位トークンは使われない** (設定でのON/OFFは無い)
- **誰が登録できるかは、このモジュールの書き込み権限がそのまま効く**。
  「本人だけが自分の行を編集できる」設計 (UserWriteCondition 等) にしておくこと

## Design

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| DbColumnToken | string | ○ | トークンを保存する DB カラム名 (書き込み専用・文字列カラム)。暗号化後の値が入るので長さに余裕を持たせる |

入力する値はトークン JSON (`{"refresh_token":"..."}`) かトークン文字列そのもの。どちらでも動く。

### JSON 例

```json
{
  "Name": "GmailToken",
  "DbColumnToken": "gmail_token",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.GmailTokenFieldDesign"
}
```

## サーバー設定 (appsettings)

```json
"Gmail": {
  "SenderMailAddress": "notify@your-domain.example",
  "ClientSecret": "...client_secret.json のパス...",
  "TokenSecret": "...システム送信者のトークン (フォールバック)...",
  "TokenEncryptionKey": "...(環境変数 Gmail__TokenEncryptionKey で与えるのが安全)..."
}
```

フィールドを置く先は**デザインの CurrentUser モジュール** (アプリ設定の「現在のユーザーのモジュール」)。
このモジュールにこのフィールドを1つ置くだけで有効になり、**フィールド名の設定は要りません** (型で判別する)。
差出人アドレスは同じモジュールの**差出人契約 (MailSenderContractField)** が宣言したものを使います (無ければデザインチェックがエラー)。

送信時、差出人のアドレスで CurrentUser モジュールを検索し、トークンが登録されていれば復号して
その人として送る。未登録なら `TokenSecret` (システム送信者) にフォールバックする。
差出人が本人になるのは MailField / BulkMailField の「自分を差出人にする」(IsFromCurrentUser) のときだけで、
アドレス指定はできない。

暗号化の方式は公開されているが (AES-GCM 256bit・ランダム nonce・保存形式 `v1:` + Base64)、
安全性は鍵だけに依存する。**鍵をリポジトリやデザインファイルに置かないこと**。
守れる範囲は DB のダンプ・バックアップ・レプリカ経由の漏えいで、サーバー自体が奪われた場合
(鍵も一緒に読める) は守れない。

## Script

スクリプト API は無い (値を読む手段を作らないため)。

## CSS

入力用の textarea (`form-control`) と、ファイル読み込み・登録解除のボタンを持つ。
`data-system="gmail-token"` 属性を持つ。
