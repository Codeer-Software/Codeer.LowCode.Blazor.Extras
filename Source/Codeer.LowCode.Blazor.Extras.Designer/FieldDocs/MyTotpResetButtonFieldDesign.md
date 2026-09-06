## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.MyTotpResetButtonFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

**ログイン中の自分**の認証アプリ (TOTP) 登録を解除するボタン。設定画面・マイページなど**どのモジュールにも置ける**。
対象は常にログイン中のユーザーで、表示中の行とは無関係。登録済みなら「認証アプリ: 登録済み」と解除ボタン、未登録なら「認証アプリ: 未登録」だけを出す。
アプリで認証アプリの二要素認証を使っていない (ログインアカウント契約に TOTP の列が無い) ときは何も出さない。

- 用途: 機種変更の前に自分で解除しておく。解除すると次回ログイン時に QR から再登録になる (弱くなる方向ではない)。確認ダイアログ付き
- 他人 (任意のユーザー) の登録を解除するのは [TotpResetButtonField](TotpResetButtonFieldDesign.md) (ログインユーザーモジュールの詳細画面に置き、表示中の行が対象。管理者用)
- 状態は表示時にサーバーへ問い合わせる

### サーバ側

テンプレート (Starter の Cookie ホスト) に組み込み済み: `GET api/account/totp/status/{userId}` / `POST api/account/totp/reset/{userId}` (自分の userId は常に許可)。
独自ホストなら同じ形のエンドポイントを用意し、`TotpResetClient.StatusEndPoint` / `ResetEndPoint` に結線する (テンプレートは `ServiceInitializer` で設定)。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / フォーカス系はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `Text` | string | `""` | ボタンの文字。空なら「認証アプリを解除」 |
| `ConfirmMessage` | string | `""` | 解除前の確認メッセージ。空なら既定 |

### JSON例

```json
{
  "Text": "",
  "ConfirmMessage": "",
  "Name": "MyTotpReset",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MyTotpResetButtonFieldDesign"
}
```

## Script

| メソッド | 説明 |
|---|---|
| `Reset()` | 確認の後、自分の登録を解除する。成功なら true |
| `IsRegistered` | 登録済みか (bool?)。未取得・機能無効は null |
