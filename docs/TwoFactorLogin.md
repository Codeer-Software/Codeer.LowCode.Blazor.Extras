# 二要素認証 (認証アプリ TOTP / メールのワンタイムコード)

`Codeer.LowCode.Blazor.Extras.Server` の `Auth` 名前空間。ID/パスワードのログイン (Starter の Cookie テンプレート) に 2 段階目を足す。方式は 2 つ。

| 方式 | 有効にする方法 | 強度・特徴 |
|---|---|---|
| 認証アプリ (TOTP) | ユーザーモジュールに `TotpSecretField` を置く | RFC 6238 (HMAC-SHA1 / 30 秒 / 6 桁)。初回ログインで QR を登録。オフラインで動く。列が 3 つ要る |
| メールのワンタイムコード | ユーザーモジュールの `LoginAccountContractField` の `TwoFactorEmail` に送信先のフィールドを設定 | 6 桁コードをメールで送る。ユーザー側の登録不要で導入が軽い。メールを受け取れる = 本人という前提。列は不要 (コードはサーバーのキャッシュ)。メール送信の設定 (docs/Mail.md) が前提 |

両方あるときは認証アプリが優先される。クライアント (login.html / MAUI の Login.razor) は `POST api/account/login` の応答の `status` で 2 段階目の種類を知る。

## メールのワンタイムコード

- 有効化: `LoginAccountContractField.TwoFactorEmail` にメールアドレスのフィールドを設定する (それだけ)
- サーバー設定 (appsettings `EmailOtpLogin`、すべて任意): `MailInfraName` (送信インフラの呼び名。空なら `Mail.DefaultInfraName`) / `Subject` (既定 "認証コード: {code}") / `Body` ({code} と {minutes} が置き換わる) / `CodeLifetimeMinutes` (既定 10) / `MaxAttempts` (既定 5。超えるとコード破棄)
- 流れ: パスワード成功 → コードを発行してメール送信 → `status: "email"` と伏せ字の送信先 (`maskedEmail`) を返す → `TwoFactorCode` 付きで再送 → `ok`
- 再送 = パスワードからやり直す (前のコードは無効になる)。同じコードは 1 回だけ。送れなかったときは `send_failed` (サインインしない)
- テンプレートは `MailDispatcher` 経由で送るので、`Mail.DebugRedirectAllTo` (開発環境の宛先リダイレクト) が効く。履歴モジュールには記録しない (コードを残さない)
- コードの置き場は `IDistributedCache` (テンプレートは `CookieAuthentication.cs` で `AddDistributedMemoryCache()`)。複数インスタンスでは Redis 等の共有キャッシュに差し替える (docs/ExternalLogin.md の「複数インスタンスの注意」)

```csharp
// AccountController.Login: パスワード検証の後 (TotpSecretField が無いときだけ)
if (accounts.HasTwoFactorEmail)
{
    var email = new EmailOtpLogin(SystemConfig.Instance.EmailOtpLogin, message => CreateMailDispatcher().SendAsync(...), _cache);
    var result = await email.VerifyAsync(account.UserId, account.TwoFactorEmail, loginInfo.TwoFactorCode);
    if (result.Status != EmailOtpLoginStatus.Ok) return Ok(result);   // email / invalid_code / send_failed: サインインしない
}
```

## 認証アプリ (TOTP)

`TotpSecretField` + `TotpLogin`。オーセンティケータアプリ (Google Authenticator / Microsoft Authenticator 等) の 6 桁コード。RFC 6238 (HMAC-SHA1 / 30 秒 / 6 桁)。QR コードは QRCoder。

## 考え方

- 有効・無効と列の場所は **デザインで決まる**: ユーザーモジュール (`AppSettings.CurrentUserModuleDesignName`) に `TotpSecretField` を置き、
  ユーザー行の 3 列 (秘密鍵 / 確認済み / 最終タイムステップ) を名指しする。フィールドを外せば従来のログインに戻る。appsettings に列名の設定は無い
- 3 列は書き込み専用 (PasswordHashField のハッシュ・ソルトと同じ)。通常のモジュール読み書きには出てこず、クライアントに秘密鍵が渡ることはない。
  ユーザーを画面で編集しても列は触られない
- パスワードの検証とサインインはこれまでどおりテンプレートの `AccountController`。パッケージ (`TotpLogin`) は「パスワードが通った後にコードを検証する」部分だけを持ち、
  表・列名はデザインから引いて SQL で直接読み書きする (ログイン時はまだ認証済みユーザーがいないため、パスワード照合と同じ経路)
- 登録は最初のログイン時。パスワードが通ったユーザーに QR を出し、そのコードが合ったときに登録が確定する
- リセット (機種変更・紛失) は 3 列を空にするだけ (`TotpLogin.ResetAsync`)

## 使い方

1. ユーザーテーブルに 3 列を足す

   ```sql
   totp_secret        TEXT    NULL,
   totp_confirmed     INTEGER NULL,
   totp_last_timestep INTEGER NULL
   ```

2. デザイナでユーザーモジュールに `TotpSecretField` を追加し、3 列を割り当てる ([フィールドの説明](../Source/Codeer.LowCode.Blazor.Extras.Designer/FieldDocs/TotpSecretFieldDesign.md))

3. 任意: appsettings でオーセンティケータに表示するアプリ名を変える (既定 "LowCodeApp")

   ```json
   { "TotpLogin": { "Issuer": "社内ポータル" } }
   ```

## ログインの流れ (API 契約)

`POST api/account/login` が 2 段階になる。1 段階目は `{ Id, Password }`、2 段階目は同じものに `TwoFactorCode` を足して送る。
応答は `{ status, secret?, otpauthUri?, qrPngBase64?, maskedEmail? }`。

| status | 意味 | クライアントの動き |
|---|---|---|
| `setup` | (認証アプリ) パスワードは正しいが未登録 | QR (`qrPngBase64`) と手入力用の鍵 (`secret`) を表示し、コードを入力させて `TwoFactorCode` 付きで再送 |
| `totp` | (認証アプリ) パスワードは正しく登録済み | コードを入力させて `TwoFactorCode` 付きで再送 |
| `email` | (メール) コードを送った | 送信先 (`maskedEmail`) を案内し、コードを入力させて `TwoFactorCode` 付きで再送 |
| `invalid_code` | コード不一致 (期限切れ・同じコードの再利用・試行超過を含む) | 入力し直し |
| `send_failed` | (メール) 送れなかった | 管理者に連絡するよう案内 |
| `ok` | サインイン済み | アプリへ |

二要素認証が無効なときも応答は `{ status: "ok" }` (以前の空応答から変わっている。login.html / MAUI の Login.razor は両方を受け付ける)。

テンプレート側のコード (同梱ソース):

```csharp
// AccountController.Login: パスワード検証の後
var totp = TotpLogin.Create(designData, SystemConfig.Instance.TotpLogin, _dataService.DbAccess);   // フィールドが無ければ null
if (totp != null)
{
    var result = await totp.VerifyAsync(account.UserId, account.LoginName, loginInfo.TwoFactorCode);
    if (result.Status != TotpLoginStatus.Ok) return Ok(result);   // setup / totp / invalid_code: サインインしない
}
```

## 検証の既定

- 照合窓は現在時刻 ±1 ステップ (30 秒ずれまで許容)
- 一度成功したタイムステップ以下のコードは受け付けない (リプレイ防止)
- 登録が確定するまでは同じ鍵で QR を再表示する (途中で閉じても登録し直しにならない)
- 未登録のユーザーがコードだけ送っても鍵は作らない

## 割り切りと補い方

- **初回ログイン時の登録**: パスワードだけを知る第三者が先に登録できる余地がある。許容できない運用では、初回ログインを管理者立ち会いで行う等で補う
- 試行回数制限・リカバリーコードは持たない。必要なら前段 (リバースプロキシ等) か AccountController で足す
- 時刻ずれはサーバーと端末の時計に依存する。サーバーは NTP で合わせておく

## ネイティブアプリ (MAUI)

同じ API を使う。テンプレートの `Pages/Login.razor` は `setup` / `totp` を受けるとコード入力に切り替わり、`setup` では QR を表示する。
