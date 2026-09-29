# EditHistoryUndeleteButtonField (編集履歴 復活ボタン)

削除されたレコードを履歴から復活させるボタン。履歴モジュール (EditHistoryContractField を置いたモジュール) の
詳細画面に置く。表示中の履歴行の ChangeType が Delete で、それがそのレコードの最新の版で、対象モジュールで削除できる人 (CanDelete と UserRead / UserWrite 条件) にだけ出る。

## Design

- `Text`: ボタンの文言。空なら「このレコードを復活」
- 契約フィールドの無いモジュールに置くとデザインチェックがエラー

## 動作

- クライアントは「削除の版の履歴行の Id」だけを送り、サーバー (EditHistoryRecorder) がその版のスナップショットからレコード全体を 1 トランザクションで戻す
- 復活できるのはそのレコードの最新の版が削除のときだけ。復活済み・作り直し済みのレコードの古い削除の版からは復活できない (ボタンも出ない・送っても失敗)
- 対象モジュール (履歴行の ModuleName) が**論理削除** (LogicalDelete / DeletedAt / Deleter のどれかがある) なら、
  レコードと親と一緒に消えた従属レコード (子・孫) を Id を保って「削除の取り消し」で戻す。Id はそのままなので、そのレコードへのリンクは切れない。
  子の行は親の削除が子を消すときと同じく、子モジュールの CanDelete だけを見る (ユーザーの権限・行の条件は見ない)
- 対象モジュールが**物理削除**なら、スナップショットから作り直す。手入力 Id (複合 Id) のモジュールは元の Id で作る (同じ Id のレコードが既にあれば復活できない)。
  自動採番のモジュールは新しい Id になり、子も親の新しい Id で作り直す。このとき旧 Id の版は新しい Id に付け替えられ、復活したレコードの履歴は最初から繋がる
- 埋め込みモジュール (ModuleField) の子は親の削除では消えないので、親の参照を版の子に戻す (子も消えていれば版の内容で作り直す)
- 復活はどちらも ChangeType = Restore の版として記録される
- 権限は削除と同じ: 対象モジュールの CanDelete と UserRead / UserWrite 条件に加え、行の条件 (DataRead / DataWrite) を削除前の内容に当てる。
  権限が無ければ失敗のメッセージが出る
- 復活後はそのレコードの詳細に遷移する (自動採番の物理削除は作り直した新しい Id)

```json
{ "Name": "UndeleteButton", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.EditHistoryUndeleteButtonFieldDesign" }
```
