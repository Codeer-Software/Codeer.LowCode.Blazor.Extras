# EditHistoryUndeleteButtonField (編集履歴 復活ボタン)

削除されたレコードを履歴から復活させるボタン。履歴モジュール (EditHistoryContractField を置いたモジュール) の
詳細画面に置く。表示中の履歴行の ChangeType が Delete で、対象モジュールで削除できる人 (CanDelete と UserWrite 条件) にだけ出る。

## Design

- `Text`: ボタンの文言。空なら「このレコードを復活」
- 契約フィールドの無いモジュールに置くとデザインチェックがエラー

## 動作

- クライアントは「削除の版の履歴行の Id」だけを送り、サーバー (EditHistoryRecorder) がその版のスナップショットからレコード全体を 1 トランザクションで戻す
- 対象モジュール (履歴行の ModuleName) が**論理削除** (LogicalDelete / DeletedAt / Deleter のどれかがある) なら、
  レコードと従属レコード (子・孫) を Id を保って「削除の取り消し」で戻す。Id はそのままなので、そのレコードへのリンクは切れない。
  復活は ChangeType = Restore の版として記録される
- 対象モジュールが**物理削除**なら、スナップショットから新しいレコードを作り直す (Id は振り直し)。親を作り直すと子の参照も付け替わるので、
  従属レコードは論理削除のモジュールでも新しい行として作り直す。作成の版として記録される
- 権限は削除の逆: 対象モジュールの CanDelete と UserWrite 条件に加え、行の条件 (DataRead / DataWrite) を削除前の内容に当てる (従属レコードの行も)。
  権限が無ければ失敗のメッセージが出る
- 復活後はそのレコードの詳細に遷移する (物理削除は作り直した新しい Id)

```json
{ "Name": "UndeleteButton", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryUndeleteButtonFieldDesign" }
```
