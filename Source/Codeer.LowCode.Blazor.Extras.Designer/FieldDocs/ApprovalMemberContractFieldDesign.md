# ApprovalMemberContractField (承認メンバー契約)

承認メンバーモジュール (承認者スナップショット) に1つ置き、「役割 → 自モジュールのフィールド名」の
マッピングを宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする
- 役割プロパティはフィールドのリネームに自動追従する
- 同じモジュールに複数置くとエラー

役割: `Flow` (Link→フロー) / `AttemptNo` / `StepNo` / `StepName` / `StepType` / `CompletionPolicy` /
`IsCommentRequiredOnReject` / `ReturnScope` / `ApproverUser` (Link→ユーザー) / `IsRequired` /
`IsFinalStep` / `Status` / `ActedAt`。

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
