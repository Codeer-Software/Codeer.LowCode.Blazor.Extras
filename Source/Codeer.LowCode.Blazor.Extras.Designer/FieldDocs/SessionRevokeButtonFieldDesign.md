## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.SessionRevokeButtonFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

表示中の行のユーザーを全端末からログアウトさせるボタン (管理者用)。**ログインユーザーモジュール** (`AppSettings.CurrentUserModuleDesignName`) の詳細画面に置く。
ログアウトは「セッションの印の列を NULL にする」データを持って**通常の Submit** で書く。そのため権限はモジュールの UserWriteCondition / 行の DataWriteCondition が
そのまま効き (その行を編集できる人だけがログアウトさせられる)、サーバーに専用の入口は要らない。

- 用途: 端末紛失・パスワード流出の疑いなどで、管理者がそのユーザーのログイン済みの Cookie を全端末まとめて使えなくする。確認ダイアログ付き
- 印が NULL の行は Cookie の照合で落ちる。次のログインで新しい印が採番される (新しいログインは止めない。止めるなら IsActive を偽にする)
- ボタンを押すとモジュール全体が保存される (SubmitButtonField と同じ)。編集中の他のフィールドも一緒に保存される
- 新規行 (ユーザーがまだ無い) では出さない

> [LoginAccountContractField](LoginAccountContractFieldDesign.md) の `DbColumnSessionStamp` が有効化の宣言。このボタンの列は**契約と同じ列**にする (違う・契約に列が無いとデザインチェックがエラーにする)。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / フォーカス系はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `Text` | string | `""` | ボタンの文字。空なら「全端末からログアウト」 |
| `ConfirmMessage` | string | `""` | ログアウト前の確認メッセージ。空なら既定 |
| `DbColumnSessionStamp` | string | `""` | セッションの印の列 (書き込み専用)。契約の DbColumnSessionStamp と同じ列 |

ログインユーザーモジュール以外に置く、または列が契約と違うとデザインチェックがエラーにする。

### JSON例

```json
{
  "DbColumnSessionStamp": "session_stamp",
  "Name": "全端末ログアウト",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.SessionRevokeButtonFieldDesign"
}
```

## Script

| メソッド | 説明 |
|---|---|
| `Revoke()` | 確認の後、表示中の行のユーザーを全端末からログアウトさせてモジュールを保存する。成功なら true |
