# LoginAccountContractField (ログインアカウント契約)

ログインユーザーモジュール (`AppSettings.CurrentUserModuleDesignName` のモジュール) に 1 つ置き、
「このモジュールの行がログインアカウントとしてどう振る舞うか」を宣言する契約フィールド。宣言は 2 種類:

- 役割 (自モジュールのフィールド参照): ログイン ID / 外部 IdP の突き合わせ / 有効フラグ / 表示名 / メール二要素の送信先
- ログイン用の DB 列 (書き込み専用。通常のモジュール読み書きには出てこず、ログイン時にサーバーが直接読み書きする): パスワードのハッシュ / ソルト、認証アプリ (TOTP) の 3 列

UI もデータも持たない。

テンプレート (Starter の Cookie ホスト) のログインはこの契約でユーザー行を扱う:

- ID/パスワードのログイン: `LoginName` の列で行を探し、同じモジュールの `PasswordHashField` の列 (ハッシュ / ソルト) で照合する
- 外部 IdP (Entra ID / Google / AWS Cognito / OIDC): IdP が確認した本人 (Entra = UPN、Google = メール) を `ExternalLoginName` の列 (空なら `LoginName` の列) と突き合わせて行に解決する
- `IsActive` が偽の行はどちらの経路でもログインできない (退職・停止)
- `DisplayName` は画面のユーザー表示 (Cookie の Name) と二要素認証の QR のアカウント名
- `TwoFactorEmail` を設定すると、メールのワンタイムコードによる二要素認証になる (TOTP の列も設定されていれば認証アプリが優先)
- 初回起動時にユーザーが 0 件なら `admin` / `admin` を作る (パスワードの列があるときだけ。表示名・有効フラグの列があれば埋める)

契約が無いとサーバーはユーザー行を解決できず、ログインできない (`LoginAccountStore.Create` が null)。
パスワードを「書く」のは [PasswordHashField](PasswordHashFieldDesign.md) (ユーザー登録画面や変更ダイアログに置き、この契約と同じ列を指す)。契約は「照合する」側。

## Design

- `LoginName` の初期値は既定フィールド名 (`LoginName`)。既定名でフィールドを作れば設定不要 (置くだけ)。他の役割は空が初期値 (= 使わない)
- 必須の役割が空・役割が指す先が無ければデザインチェックがエラーにする。リネームに自動追従する
- 指す先は DB 列を持つ値フィールド (TextField / BooleanField 等) であること

| 役割 (表示名) | 内容 | 必須 |
|---|---|---|
| LoginName (ログイン ID (必須)) | ログイン画面で入力する ID を持つフィールド (ユーザー名・メール等) | ○ |
| ExternalLoginName (外部 IdP の突き合わせ) | 外部 IdP の本人 (UPN・メール) と比較するフィールド。空なら LoginName と同じ列。社内のログイン ID とメールが別のときにメールのフィールドを指定する | - |
| IsActive (有効フラグ) | 偽ならログイン拒否 (Boolean / 0・1 の数値 / "true"・"1" の文字列)。NULL は無効扱い。空なら判定しない | - |
| DisplayName (表示名) | 画面のユーザー表示と TOTP の QR に使う名前。空なら LoginName | - |
| TwoFactorEmail (二要素認証のメール送信先) | 設定するとパスワード成功後にワンタイムコードをこのフィールドのメールアドレスへ送り、入力を求める (メールの体裁・有効期限はサーバー設定 `EmailOtpLogin`)。TOTP の列も設定されていれば認証アプリが優先。空なら無効 | - |

### ログイン用の DB 列 (書き込み専用)

| プロパティ (表示名) | 内容 |
|---|---|
| DbColumnPasswordHash / DbColumnPasswordSalt (パスワードのハッシュ列 / ソルト列 (照合用)) | ID/パスワードのログインで照合する列。PasswordHashField の DbColumnHash / DbColumnSalt と同じ列を指す。両方設定するか両方空 (空 = パスワードログイン無し = 外部 IdP 専用)。デザインチェックで片方だけはエラー |
| DbColumnTotpSecret / DbColumnTotpConfirmed / DbColumnTotpLastTimestep (認証アプリ: 秘密鍵列 / 確認済み列 / 最終タイムステップ列) | 認証アプリ (TOTP) の二要素認証。3 列そろえて設定すると有効 (初回ログインで QR を登録)。空 = 無効。1〜2 列だけはエラー。列は `TEXT` / `INTEGER` / `INTEGER` |

列は実テーブルに存在するかデザインチェックで検証される。

## JSON例

```json
{
  "LoginName": "UserName",
  "ExternalLoginName": "Email",
  "IsActive": "IsActive",
  "DisplayName": "Name",
  "TwoFactorEmail": "Email",
  "DbColumnPasswordHash": "hash",
  "DbColumnPasswordSalt": "salt",
  "DbColumnTotpSecret": "totp_secret",
  "DbColumnTotpConfirmed": "totp_confirmed",
  "DbColumnTotpLastTimestep": "totp_last_timestep",
  "Name": "LoginAccount",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.LoginAccountContractFieldDesign"
}
```

同じモジュールに `IdField` (ユーザー ID) と役割のフィールドを置く。パスワードを設定する画面 (ユーザー登録・変更ダイアログ) には `PasswordField` + `PasswordHashField`。

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
