# ApprovalFlowContractField (承認フロー契約)

承認フローモジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを宣言するフィールド。
UI もデータも持たない (DB列不要)。ApprovalFlowField はフローモジュール名だけを指定し、
フィールド名の解決はすべてこの契約経由で行う。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする (契約の実装漏れ検出)
- 役割プロパティはフィールドのリネームに自動追従する (フィールド名は後から自由に変えられる)
- 同じモジュールに複数置くとエラー

役割: `Status` / `TargetModuleName` / `TargetId` / `RouteName` / `Applicant` (Link→ユーザー) /
`AttemptNo` / `CurrentStepNo` / `Members` / `Histories`。

`Members` / `Histories` は一覧フィールド (バインド `Flow.Value == Id.Value`) を指し、
その参照先がメンバーモジュール・履歴モジュールになる (申請書側でのモジュール名指定は不要)。
参照先モジュールには対応する契約フィールド (ApprovalMemberContractField / ApprovalHistoryContractField) が必要。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalFlowContractFieldDesign" }
```

必須フィールドの構成や権限設定など全体像は ApprovalFlowField のドキュメントを参照。
