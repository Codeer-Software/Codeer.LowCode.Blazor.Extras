# ApprovalRouteStepContractField (承認経路ステップ契約)

経路マスタ (ステップ) モジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを
宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- **役割を空にすると「使わない」宣言** (チェック対象外。LoadRoute はスクリプト組み立てと同じ既定値に倒す)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする

役割: `Route` (Link→経路) / `StepNo` (並び順・数値) / `StepName` / `StepType` (空=承認) /
`CompletionPolicy` (空=必須メンバー) / `IsCommentRequiredOnReject` (空=true) /
`ReturnScope` (空=申請者のみ) / `Members` (List→ステップ承認者) / `ApproverUser` (ステップ直付けの承認者Link)。

## 承認者の2形態

- **複数人 (既定)**: `Members` 一覧の参照先が承認者モジュールになる (参照先に
  ApprovalRouteStepMemberContractField が必要)。B or C (どちらでも) や全員承認をマスタで表現できる
- **シンプル (1ステップ1人)**: `Members` を空にして `ApproverUser` (ステップ行に直付けのユーザーLink) を
  設定する。承認者モジュールも入れ子一覧も不要になり、ステップ一覧だけで管理できる。
  StepType / CompletionPolicy 等も空にすれば、ステップ = 「名前 + 承認者」だけの最小構成にできる

どちらか一方は必須 (両方空はデザインチェックがエラーにする)。

```json
// シンプル構成の契約例 (使わない役割は空)
{
  "Name": "Contract",
  "StepType": "", "CompletionPolicy": "", "IsCommentRequiredOnReject": "", "ReturnScope": "",
  "Members": "",
  "ApproverUser": "Approver",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalRouteStepContractFieldDesign"
}
```

経路マスタの全体像は ApprovalRouteContractField のドキュメントを参照。
