# EditHistoryTargetLinkField (編集履歴 対象レコードリンク)

履歴行の対象レコード (ModuleName / DataId) の詳細を開くリンク。履歴モジュール (EditHistoryContractField を置いたモジュール) の
一覧の列や詳細に置く。「この履歴はどのレコードのものか」から本体へ飛ぶ動線。

## Design

- `Text`: リンクの文言。空なら「開く」
- 契約フィールドの無いモジュールに置くとデザインチェックがエラー

## 動作

- 遷移先は対象モジュールの詳細 (`/<PageFrame>/<ModuleName>/<DataId>`)。契約の ModuleName / DataId を読む
- 出ない行: 削除の版 (レコードはもう開けない。復活は EditHistoryRestoreButtonField)、対象モジュールがデザインに無い行、未保存の行
- 履歴モジュールは通常「誰も書けない」= 行が表示専用になるが、遷移するだけなので表示専用でも使える (スクリプトでの解除は不要)
- 一覧の列に置いたときはリンクのクリックで行の選択を起こさない

```json
{ "Name": "OpenTarget", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryTargetLinkFieldDesign" }
```

一覧の列に置く例 (ListLayouts の Elements に `{ "FieldName": "OpenTarget", "Label": "" }`)。
