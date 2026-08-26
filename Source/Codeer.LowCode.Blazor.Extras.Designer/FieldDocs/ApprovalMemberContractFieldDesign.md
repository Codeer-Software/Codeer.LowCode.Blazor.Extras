# ApprovalMemberContractField (承認メンバー契約)

承認メンバーモジュール (承認者スナップショット) に1つ置き、「役割 → 自モジュールのフィールド名」の
マッピングを宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする
- 役割プロパティはフィールドのリネームに自動追従する
- 同じモジュールに複数置くとエラー
- 必須の役割 (デザイナの表示名に「(必須)」) はエンジンがメンバーの特定と状態遷移に使うもの。空・名指ししたフィールドの不在はデザインチェックがエラー
- 任意の役割は空にできる (= このアプリではその項目を使わない)。名指しした場合のフィールド不在はエラー
  - 表示・スナップショット系 (StepName / IsFinalStep / ActedAt): 空なら書かれない。UI はそれ無しで表示する (ステップ名はステップ番号で代替)
  - ポリシー系 (CompletionPolicy / ReturnScope / IsCommentRequiredOnReject / IsRequired): 空ならエンジンは **既定値** で動く。
    経路マスタ側に同名の設定があってもメンバー行に写らない (= 既定で動く) ので、その概念を使わないアプリだけ空にする

| 役割 (表示名) | 内容 | 必須 | 空のときの既定 |
|---|---|---|---|
| Flow (フロー) | Link→フロー行 | ○ | |
| AttemptNo (試行番号) | 展開時の試行番号 | ○ | |
| StepNo (ステップ番号) | 1 始まり | ○ | |
| StepName (ステップ名) | 表示用のステップ名 | - | ステップ番号を表示 |
| StepType (ステップ種別) | 承認 / 確認 (`ApprovalStepType`) | ○ | |
| CompletionPolicy (ステップ完了条件) | Select (`ApprovalCompletionPolicy`) | - | `RequiredMembers` (必須メンバー全員承認) |
| IsCommentRequiredOnReject (却下時のコメント必須) | Boolean | - | `false` |
| ReturnScope (差し戻し先の範囲) | Select (`ApprovalReturnScope`) | - | `ApplicantOnly` (申請者へのみ) |
| ApproverUser (承認者ユーザー) | Link→ユーザー | ○ | |
| IsRequired (必須承認者か) | Boolean | - | `true` (全員必須) |
| IsFinalStep (最終承認ステップか) | 条件式で「最終承認者」を表すためのスナップショット | - | 書かれない |
| Status (メンバーの状態) | Select (`ApprovalMemberStatus`) | ○ | |
| ActedAt (操作日時) | 承認・却下した日時 (表示用) | - | 書かれない |
| TurnNotifyMail (順番到達通知メール) | 自モジュールの MailField 名。下記 | - | 通知しない |


```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalMemberContractFieldDesign" }
```

### 順番到達の通知メール (TurnNotifyMail)

`TurnNotifyMail` に自モジュールの MailField 名を設定すると、申請・承認・差し戻しで
**承認の順番が回ってきた (Waiting になった) メンバー**へ、サーバーが自動で通知メールを送る。
空 (既定) = 通知しない。

- テンプレートの変数はメンバー行で解決される (`{StepName.Value}` や
  リンクパス `{ApproverUser.Name.Value}` / 宛先 `ApproverUser.Email.Value` 等)
- 送信は操作のコミット後。**通知の失敗は承認操作を失敗させない** (サーバーログのみ)
- アドレスが空のメンバーはスキップされる。送信履歴 (Mail.HistoryModuleName) にも記録される
- サーバー側は ApprovalEngine の `MailDispatcher` プロパティの結線が必要 (テンプレートの ApprovalController 参照)

```json
{ "Name": "Contract", "TurnNotifyMail": "TurnMail",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalMemberContractFieldDesign" },
{ "Name": "TurnMail", "ToVariable": "ApproverUser.Email.Value",
  "Subject": "【承認依頼】{StepName.Value} の承認をお願いします",
  "Body": "{ApproverUser.Name.Value} さん\n承認の順番が回ってきました。",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MailFieldDesign" }
```

必須フィールドの構成や権限設定など全体像は ApprovalFlowField のドキュメントを参照。
