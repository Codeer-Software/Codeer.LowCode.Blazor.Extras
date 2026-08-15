# ApprovalHistoryContractField (承認履歴契約)

承認履歴モジュール (監査履歴) に1つ置き、「役割 → 自モジュールのフィールド名」の
マッピングを宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする
- 役割プロパティはフィールドのリネームに自動追従する
- 同じモジュールに複数置くとエラー

役割: `Flow` (Link→フロー) / `AttemptNo` / `StepNo` / `Action` / `ActorUser` (Link→ユーザー) /
`FromStatus` / `ToStatus` / `Comment` / `ActedAt`。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalHistoryContractFieldDesign" }
```

必須フィールドの構成や権限設定など全体像は ApprovalFlowField のドキュメントを参照。
