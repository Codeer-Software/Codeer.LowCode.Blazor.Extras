# LoginAccountContractField (ログインアカウント契約)

ログインユーザーモジュール (`AppSettings.CurrentUserModuleDesignName` のモジュール) に 1 つ置き、
「このモジュールの行がログインアカウントとしてどう振る舞うか」を宣言する契約フィールド。宣言は 3 種類:

- 役割 (自モジュールのフィールド参照): ログイン ID / 外部 IdP の突き合わせ / 有効フラグ / 表示名 / メール二要素の送信先
- パスワードの入力フィールド (`PasswordField`): 指定すると、保存時にサーバーがその平文からハッシュ / ソルトを作って下の列に書く。**PasswordHashField は要らない**
- ログイン用の DB 列: パスワードのハッシュ / ソルト (書き込み専用列)、認証アプリ (TOTP) の 3 列 (列名だけの宣言。サーバーのログイン処理が直接読み書きする)

UI は持たない。データはハッシュ / ソルトの 2 列分だけで、保存時にサーバーが差し込む (クライアントは送受信しない)。

テンプレート (Starter の Cookie ホスト) のログインはこの契約でユーザー行を扱う:

- ID/パスワードのログイン: `LoginName` の列で行を探し、契約のハッシュ / ソルト列で照合する
- 外部 IdP (Entra ID / Google / AWS Cognito / OIDC): IdP が確認した本人 (Entra = UPN、Google = メール) を `ExternalLoginName` の列 (空なら `LoginName` の列) と突き合わせて行に解決する
- `IsActive` が偽の行はどちらの経路でもログインできない (退職・停止)
- `DisplayName` は画面のユーザー表示 (Cookie の Name) と二要素認証の QR のアカウント名
- `TwoFactorEmail` を設定すると、メールのワンタイムコードによる二要素認証になる (TOTP の列も設定されていれば認証アプリが優先)
- 初回起動時にユーザーが 0 件なら `admin` / `admin` を作る (パスワードの列があるときだけ。表示名・有効フラグの列があれば埋める)

契約が無いとサーバーはユーザー行を解決できず、ログインできない (`LoginAccountStore.Create` が null)。
パスワードを「書く」のも契約 (`PasswordField` を指定したとき。ユーザー登録・編集画面の PasswordField から)。契約の無いモジュール (別モジュールのパスワード変更ダイアログなど) では [PasswordHashField](PasswordHashFieldDesign.md) を使う。

## Design

- `LoginName` は必須 (空だとデザインチェックがエラーにする)。他の役割は空が初期値 (= 使わない)
- 必須の役割が空・役割が指す先が無ければデザインチェックがエラーにする。リネームに自動追従する
- 指す先は DB 列を持つ値フィールド (TextField / BooleanField 等) であること

| 役割 (表示名) | 内容 | 必須 |
|---|---|---|
| LoginName (ログイン ID (必須)) | ログイン画面で入力する ID を持つフィールド (ユーザー名・メール等) | ○ |
| ExternalLoginName (外部 IdP の突き合わせ) | 外部 IdP の本人 (UPN・メール) と比較するフィールド。空なら LoginName と同じ列。社内のログイン ID とメールが別のときにメールのフィールドを指定する | - |
| IsActive (有効フラグ) | 偽ならログイン拒否 (Boolean / 0・1 の数値 / "true"・"1" の文字列)。NULL は無効扱い。空なら判定しない | - |
| DisplayName (表示名) | 画面のユーザー表示と TOTP の QR に使う名前。空なら LoginName | - |
| TwoFactorEmail (二要素認証のメール送信先) | 設定するとパスワード成功後にワンタイムコードをこのフィールドのメールアドレスへ送り、入力を求める (メールの体裁・有効期限はサーバー設定 `EmailOtpLogin`)。TOTP の列も設定されていれば認証アプリが優先。空なら無効 | - |
| PasswordField (パスワード入力フィールド) | 同じモジュールの `PasswordField` (平文の DB 列は持たない)。値があれば保存時にサーバーがハッシュ / ソルトを作って `DbColumnPasswordHash` / `DbColumnPasswordSalt` に書く (テンプレートの `CustomizedModuleDataIO` が `PasswordHashHelper.ApplyPasswordHash` を呼ぶ)。空なら書かない (外部 IdP 専用、または PasswordHashField で書く)。指定するときは列 2 つが必須。同じモジュールに PasswordHashField があるとエラー (同じ列を二重に書く) | - |

### ログイン用の DB 列 (書き込み専用)

| プロパティ (表示名) | 内容 |
|---|---|
| DbColumnPasswordHash / DbColumnPasswordSalt (パスワードのハッシュ列 / ソルト列) | ID/パスワードのログインで照合する列 (書き込み専用)。`PasswordField` を指定していれば保存時にここへ書く。両方設定するか両方空 (空 = パスワードログイン無し = 外部 IdP 専用)。デザインチェックで片方だけはエラー |
| DbColumnTotpSecret / DbColumnTotpConfirmed / DbColumnTotpLastTimestep (認証アプリ: 秘密鍵列 / 確認済み列 / 最終タイムステップ列) | 認証アプリ (TOTP) の二要素認証。3 列そろえて設定すると有効 (初回ログインで QR を登録)。空 = 無効。1〜2 列だけはエラー。列名だけの宣言で、通常の読み書きには乗らない (DDL 生成の対象外なので下の DDL で作る) |

列は実テーブルに存在するかデザインチェックで検証される。

#### 必要な DB 列 (SQLite 例)

```sql
hash               TEXT NULL,     -- パスワードのハッシュ (base64)
salt               TEXT NULL,     -- ソルト (base64)
totp_secret        TEXT NULL,     -- 認証アプリを使うときだけ
totp_confirmed     INTEGER NULL,
totp_last_timestep INTEGER NULL
```

## JSON例

```json
{
  "LoginName": "UserName",
  "ExternalLoginName": "Email",
  "IsActive": "IsActive",
  "DisplayName": "Name",
  "TwoFactorEmail": "Email",
  "PasswordField": "Password",
  "DbColumnPasswordHash": "hash",
  "DbColumnPasswordSalt": "salt",
  "DbColumnTotpSecret": "totp_secret",
  "DbColumnTotpConfirmed": "totp_confirmed",
  "DbColumnTotpLastTimestep": "totp_last_timestep",
  "Name": "LoginAccount",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.LoginAccountContractFieldDesign"
}
```

同じモジュールに `IdField` (ユーザー ID)・役割のフィールド・`PasswordField` (平文入力欄。DB 列なし) を置く。パスワードのハッシュ化は契約が行うので PasswordHashField は置かない。別モジュールのパスワード変更ダイアログ (同じテーブルを参照) には `PasswordField` + `PasswordHashField`。

## サーバ側 (テンプレートに組み込み済み)

`Codeer.LowCode.Blazor.Extras.Server.Auth.LoginAccountStore`:

```csharp
var accounts = LoginAccountStore.Create(designData, dataService.DbAccess);   // 契約が無ければ null
var account = accounts?.HasPassword == true ? await accounts.VerifyPasswordAsync(loginInfo.Id, loginInfo.Password) : null;   // ID/パスワード
var account2 = accounts == null ? null : await accounts.FindByExternalLoginNameAsync(identity.LoginName);                     // 外部 IdP の解決
// account.UserId → NameIdentifier、account.DisplayName → Name、account.LoginName → TOTP の QR
```

## Script

ランタイム Field は値・データ系メソッドを公開しない。固有のスクリプト API は無い。
