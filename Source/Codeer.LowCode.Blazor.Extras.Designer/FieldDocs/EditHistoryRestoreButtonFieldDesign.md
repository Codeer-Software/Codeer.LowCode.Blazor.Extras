# EditHistoryRestoreButtonField (編集履歴 復活ボタン)

削除されたレコードを履歴から復活させるボタン。履歴モジュール (EditHistoryContractField を置いたモジュール) の
詳細画面に置く。表示中の履歴行の ChangeType が Delete のときだけ押せる (それ以外では出ない)。

## Design

- `Text`: ボタンの文言。空なら「このレコードを復活」
- 契約フィールドの無いモジュールに置くとデザインチェックがエラー

## 動作

- 対象モジュール (履歴行の ModuleName) が**論理削除** (LogicalDelete / DeletedAt / Deleter のどれかがある) なら、
  削除の版のスナップショットにある Id (レコードと従属レコード・孫) を「削除の取り消し」で戻す。
  Id はそのままなので、そのレコードへのリンクは切れない。復活は ChangeType = Restore の版として記録される
- 対象モジュールが**物理削除**なら、スナップショットから新しいレコードを作って保存する (Id は振り直し・明細も新しい行)。
  作成の版として記録される
- どちらも対象モジュールの権限で動く (取り消しは削除の権限 = CanDelete と UserWrite 条件、新規作成は作成の権限)
- 復活後はそのレコードの詳細に遷移する

```json
{ "Name": "RestoreButton", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryRestoreButtonFieldDesign" }
```
