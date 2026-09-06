## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TotpResetButtonFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

表示中の行のユーザーの認証アプリ (TOTP) 登録を解除するボタン。**ログインユーザーモジュール** (`AppSettings.CurrentUserModuleDesignName`) の詳細画面に置く。
解除は「TOTP の 3 列を空にする」データを持って**通常の Submit** で書く。そのため権限はモジュールの UserWriteCondition / 行の DataWriteCondition が
そのまま効き (その行を編集できる人だけが解除できる)、サーバーに専用の入口は要らない。

- 用途: 端末紛失・機種変更で管理者が解除する。解除すると次回ログイン時に QR から再登録になる (弱くなる方向ではない)。確認ダイアログ付き
- 3 列は書き込み専用なので秘密鍵はクライアントに来ない。登録済みかどうかも表示しない (ボタンだけ)
- ボタンを押すとモジュール全体が保存される (SubmitButtonField と同じ)。編集中の他のフィールドも一緒に保存される
- 新規行 (ユーザーがまだ無い) では出さない
- 本人が自分の登録を解除するボタンを設定画面などに置きたいときは [MyTotpResetButtonField](MyTotpResetButtonFieldDesign.md) (どのモジュールにも置ける)

> [LoginAccountContractField](LoginAccountContractFieldDesign.md) の TOTP 列 (DbColumnTotpSecret 等) が有効化の宣言。このボタンの 3 列は**契約と同じ列**にする (違うとデザインチェックがエラーにする)。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / フォーカス系はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `Text` | string | `""` | ボタンの文字。空なら「認証アプリを解除」 |
| `ConfirmMessage` | string | `""` | 解除前の確認メッセージ。空なら既定 |
| `DbColumnTotpSecret` | string | `""` | 秘密鍵の列 (書き込み専用)。契約の DbColumnTotpSecret と同じ列 |
| `DbColumnTotpConfirmed` | string | `""` | 確認済みの列 (書き込み専用)。契約の DbColumnTotpConfirmed と同じ列 |
| `DbColumnTotpLastTimestep` | string | `""` | 最終タイムステップの列 (書き込み専用)。契約の DbColumnTotpLastTimestep と同じ列 |

ログインユーザーモジュール以外に置く、または列が契約と違うとデザインチェックがエラーにする。

### JSON例

```json
{
  "Text": "",
  "ConfirmMessage": "",
  "DbColumnTotpSecret": "totp_secret",
  "DbColumnTotpConfirmed": "totp_confirmed",
  "DbColumnTotpLastTimestep": "totp_last_timestep",
  "Name": "TotpReset",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TotpResetButtonFieldDesign"
}
```

## Script

| メソッド | 説明 |
|---|---|
| `Reset()` | 確認の後、表示中の行のユーザーの登録を解除してモジュールを保存する。成功なら true |
