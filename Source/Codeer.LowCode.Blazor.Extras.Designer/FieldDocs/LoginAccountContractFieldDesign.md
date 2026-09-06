# LoginAccountContractField (ログインアカウント契約)

ログインユーザーモジュール (`AppSettings.CurrentUserModuleDesignName` のモジュール) に 1 つ置き、
「このモジュールの行がログインアカウントとしてどう振る舞うか」を役割 → 自モジュールのフィールド名で宣言する契約フィールド。
UI もデータも持たない (DB 列不要)。

テンプレート (Starter の Cookie ホスト) のログインはこの契約でユーザー行を扱う:

- ID/パスワードのログイン: `LoginName` の列で行を探し、同じモジュールの `PasswordHashField` の列 (ハッシュ / ソルト) で照合する
- 外部 IdP (Entra ID / Google / AWS Cognito / OIDC): IdP が確認した本人 (Entra = UPN、Google = メール) を `ExternalLoginName` の列 (空なら `LoginName` の列) と突き合わせて行に解決する
- `IsActive` が偽の行はどちらの経路でもログインできない (退職・停止)
- `DisplayName` は画面のユーザー表示 (Cookie の Name) と二要素認証の QR のアカウント名
- 初回起動時にユーザーが 0 件なら `admin` / `admin` を作る (`PasswordHashField` があるときだけ。表示名・有効フラグの列があれば埋める)

パスワードを使わない構成 (外部 IdP 専用) でもログイン ID の宣言は要るので、`PasswordHashField` ではなくこの契約が持つ。
契約が無いとサーバーはユーザー行を解決できず、ログインできない (`LoginAccountStore.Create` が null)。
ハッシュ / ソルト (`PasswordHashField`) と TOTP の列 (`TotpSecretField`) は書き込み専用列の宣言を兼ねるのでフィールドのまま (同じモジュールから型で見つける)。

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

## JSON例

```json
{
  "LoginName": "UserName",
  "ExternalLoginName": "Email",
  "IsActive": "IsActive",
  "DisplayName": "Name",
  "Name": "LoginAccount",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.LoginAccountContractFieldDesign"
}
```

同じモジュールに `IdField` (ユーザー ID)、役割のフィールド、パスワードログインを使うなら `PasswordField` + `PasswordHashField`、
二要素認証を使うなら `TotpSecretField` を置く。

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
