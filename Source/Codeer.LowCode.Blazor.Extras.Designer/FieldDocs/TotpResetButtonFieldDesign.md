## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.TotpResetButtonFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

認証アプリ (TOTP) の登録を解除するボタン。**ログインユーザーモジュール** (`AppSettings.CurrentUserModuleDesignName`) の詳細画面に置く。
表示中の行のユーザーが登録済みなら「認証アプリ: 登録済み」と解除ボタン、未登録なら「認証アプリ: 未登録」だけを出す。
アプリで認証アプリの二要素認証を使っていない (ログインアカウント契約に TOTP の列が無い) ときや新規行では何も出さない。

- 自分の行 = 本人による解除 (機種変更前など)。他人の行 = 管理者による解除 (端末紛失など)
- 他人の行を解除できるのは**その行を編集できる人**だけ。サーバーが CLB の権限モデル (モジュールの UserWriteCondition / DataWriteCondition) で判定するので、権限の設定を増やさない
- 解除すると次回ログイン時に QR から再登録になる (弱くなる方向ではない)。確認ダイアログ付き
- 状態は表示時にサーバーへ問い合わせる (TOTP の列は書き込み専用でクライアントには来ない)
- 新規登録はこのボタンでは行わない (登録はログイン時の QR)
- 自分の登録だけを解除するボタンを設定画面などに置きたいときは [MyTotpResetButtonField](MyTotpResetButtonFieldDesign.md) (どのモジュールにも置ける)

> [LoginAccountContractField](LoginAccountContractFieldDesign.md) の TOTP 列 (DbColumnTotpSecret 等) が有効化の宣言。このボタンは解除の入口。

### サーバ側

テンプレート (Starter の Cookie ホスト) に組み込み済み: `GET api/account/totp/status/{userId}` / `POST api/account/totp/reset/{userId}`。
独自ホストなら同じ形のエンドポイントを用意し、`TotpResetClient.StatusEndPoint` / `ResetEndPoint` (静的プロパティ) に結線する (テンプレートは `ServiceInitializer` で設定)。
未結線・未対応のホストではボタンは何も出さない (状態が取れない)。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / フォーカス系はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `Text` | string | `""` | ボタンの文字。空なら「認証アプリを解除」 |
| `ConfirmMessage` | string | `""` | 解除前の確認メッセージ。空なら既定 |

ログインユーザーモジュール以外に置くとデザインチェックがエラーにする。

### JSON例

```json
{
  "Text": "",
  "ConfirmMessage": "",
  "Name": "TotpReset",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.TotpResetButtonFieldDesign"
}
```

## Script

| メソッド | 説明 |
|---|---|
| `Reset()` | 確認の後、表示中の行のユーザーの登録を解除する。成功なら true |
| `IsRegistered` | 登録済みか (bool?)。未取得・機能無効・新規行は null |
