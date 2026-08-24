# MailTokenField (メールトークン)

メール送信用の**ユーザー単位トークン** (Gmail ユーザー同意モードのリフレッシュトークン) を保存するフィールド。
ユーザーモジュール (AppUser 等) に置くと、動的 From の差出人ごとに「その人のトークンで・その人として」
メールを送れるようになる (送信済みが本人の Gmail に残る)。

## 仕組み (PasswordHashField と同じ書き込み専用パターン)

- トークンは**書き込み専用の DB 列**に保存され、**クライアントには一切返さない**
  (トークンは「所持 = そのユーザーとして送信できる」秘密のため。通常のフィールドに入れてはいけない)
- 入力は同じモジュール内の **PasswordField** (`TokenInputFieldName` で参照。DbColumn は空でよい =
  入力専用) に貼り付ける。**空のまま保存すると既存トークンを維持**する (パスワード変更欄と同じ挙動)
- 保存時の書き込みはサーバー側ヘルパが行う:
  `CustomizedModuleDataIO.AddAsync / UpdateAsync` で
  `Codeer.LowCode.Blazor.Extras.Services.MailUserTokenHelper.ApplyMailToken(moduleDesign, data)` を呼ぶこと
  (PasswordHashHelper.ApplyPasswordHash と並べて呼ぶのが定石)
- 送信時の読み取りはサーバー内部経路のみ (`MailUserTokenStore`。テンプレートの MailController が結線)

## Design

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 |
| TokenInputFieldName | string | ○ | トークンを貼り付ける同一モジュール内の PasswordField 名 |
| DbColumnToken | string | ○ | トークンを保存する DB カラム名 (書き込み専用・文字列カラム) |

保存する値はトークン JSON (`{"refresh_token":"..."}`) かトークン文字列そのもの。どちらでも動く。

## サーバー設定 (appsettings)

GmailApi インフラ (ユーザー同意モード) に検索先を指定する:

```json
"Mail": {
  "UserModuleName": "AppUser",
  "UserEmailFieldName": "Email",
  "Infras": [
    { "Name": "GmailApi", "Type": "GmailApi",
      "ClientSecret": "...client_secret.json のパス...",
      "TokenSecret": "...システム送信者のトークン (フォールバック)...",
      "UserTokenFieldName": "GmailToken" } ] }
```

送信時、差出人のアドレスで `Mail.UserModuleName` を検索し、トークンが登録されていれば
その人として送る。未登録なら `TokenSecret` (システム送信者) にフォールバックする。
差出人は「自分を差出人にする」(IsFromCurrentUser) でのみ本人になる (アドレス指定は不可)。

## Script

スクリプト API は無い (UI もデータも持たない)。
