## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TotpSecretFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

ID/パスワードのログインに**二要素認証（TOTP、オーセンティケータアプリの 6 桁コード）**を足すための宣言フィールド。
ユーザーモジュール（`AppSettings.CurrentUserModuleDesignName` のモジュール）に置き、ユーザー行に持たせる 3 つの DB カラム
（秘密鍵 / 確認済み / 最終タイムステップ）を名指しする。3 カラムとも**書き込み専用**（通常のモジュール読み書きには出てこない）。

このフィールドがユーザーモジュールにあると、テンプレート（Starter の Cookie ホスト）のログインが自動で 2 段階になる:
パスワードが通った後、未登録なら QR を表示して登録、登録済みならコードを要求する。フィールドを外せば従来のログインに戻る。

> [PasswordHashField](PasswordHashFieldDesign.md) と同じ作法: 入力欄も submit データも持たず、読み書きはサーバー側
> （`Codeer.LowCode.Blazor.Extras.Server.Auth.TotpLogin`）がログイン時に直接行う。

### サーバ側

テンプレートの `AccountController.Login` に組み込み済み。独自ホストなら次を呼ぶ:

```csharp
var totp = TotpLogin.Create(designData, SystemConfig.Instance.TotpLogin, dataService.DbAccess);   // フィールドが無ければ null
if (totp != null)
{
    var result = await totp.VerifyAsync(user.Id, user.UserName, loginInfo.TotpCode);
    if (result.Status != TotpLoginStatus.Ok) return Ok(result);   // setup / totp / invalid_code: サインインしない
}
```

appsettings の `TotpLogin` セクションは表示用の `Issuer`（オーセンティケータに出るアプリ名）だけ。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `DbColumnSecret` | string | `""` | 秘密鍵（Base32 文字列、32 文字）を保存する DB カラム名。未登録は NULL。**書き込み専用**。 |
| `DbColumnConfirmed` | string | `""` | オーセンティケータのコードで確認できたかを保存する整数カラム（0/1）。**書き込み専用**。 |
| `DbColumnLastTimestep` | string | `""` | 最後に成功したタイムステップを保存する整数カラム。同じコードの再利用を防ぐ。**書き込み専用**。 |

3 カラムとも実テーブルに存在するかデザインチェックで検証される。

### 必要な DB 構成

ユーザーテーブルに 3 カラムを足す。

```sql
totp_secret        TEXT    NULL,
totp_confirmed     INTEGER NULL,
totp_last_timestep INTEGER NULL
```

リセット（機種変更・紛失）はこの 3 カラムを空にする（`TotpLogin.ResetAsync`）。ユーザー管理画面からリセットさせたければ、
この列を持つ別モジュールを作らず、スクリプトや C# から `ResetAsync` を呼ぶ入口を用意する。

### JSON例

```json
{
  "DbColumnSecret": "totp_secret",
  "DbColumnConfirmed": "totp_confirmed",
  "DbColumnLastTimestep": "totp_last_timestep",
  "Name": "TotpSecret",
  "IgnoreModification": false,
  "OnValidateInput": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TotpSecretFieldDesign"
}
```

ユーザーモジュールに `IdField`（ユーザー ID 列）があることが前提（ログイン時はその列でユーザー行を特定する）。

## Script

ランタイム Field は値・データ系メソッドを公開しない（`IsModified` は常に `false`）。固有のスクリプト API は無い。
