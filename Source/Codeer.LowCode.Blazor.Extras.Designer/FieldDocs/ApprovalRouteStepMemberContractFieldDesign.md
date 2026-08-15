# ApprovalRouteStepMemberContractField (承認経路承認者契約)

経路マスタ (ステップ承認者) モジュールに1つ置き、「役割 → 自モジュールのフィールド名」の
マッピングを宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする

役割: `Step` (Link→ステップ) / `ApproverUser` (Link→ユーザー) / `IsRequired` (空=必須)。
承認者未選択の行は LoadRoute が読み飛ばす (マスタの編集途中を許容)。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalRouteStepMemberContractFieldDesign" }
```

経路マスタの全体像は ApprovalRouteContractField のドキュメントを参照。
