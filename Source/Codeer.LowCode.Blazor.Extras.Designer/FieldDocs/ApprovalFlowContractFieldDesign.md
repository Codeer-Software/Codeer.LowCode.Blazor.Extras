# ApprovalFlowContractField (承認フロー契約)

承認フローモジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを宣言するフィールド。
UI もデータも持たない (DB列不要)。ApprovalFlowField はフローモジュール名だけを指定し、
フィールド名の解決はすべてこの契約経由で行う。

## Design

- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする (契約の実装漏れ検出)
- 役割プロパティはフィールドのリネームに自動追従する (フィールド名は後から自由に変えられる)
- 同じモジュールに複数置くとエラー
- 役割は**エンジン・UI が読むものだけ**なので全て必須 (デザイナの表示名に「(必須)」)。空・名指ししたフィールドの不在はデザインチェックがエラー。
  書くだけの項目 (経路名など) は契約に持たない。必要ならフローモジュールに自由にフィールドを足してよい (エンジンは触らない)

| 役割 (表示名) | 内容 |
|---|---|
| Status (ステータス) | フローの状態 (`ApprovalFlowStatus`) |
| TargetModuleName (申請書のモジュール名) | 申請書レコードのモジュール名 |
| TargetId (申請書の Id) | 申請書レコードの Id |
| Applicant (申請者) | Link→ユーザー。申請時にエンジンが書く |
| AttemptNo (試行番号) | 再申請で +1 |
| CurrentStepNo (現在のステップ番号) | 承認待ちのステップ番号 |
| Members (承認メンバー一覧) | ListField。参照先がメンバーモジュール |
| Histories (承認履歴一覧) | ListField。参照先が履歴モジュール |


`Members` / `Histories` は一覧フィールド (バインド `Flow.Value == Id.Value`) を指し、
その参照先がメンバーモジュール・履歴モジュールになる (申請書側でのモジュール名指定は不要)。
参照先モジュールには対応する契約フィールド (ApprovalMemberContractField / ApprovalHistoryContractField) が必要。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.ApprovalFlowContractFieldDesign" }
```

必須フィールドの構成や権限設定など全体像は ApprovalFlowField のドキュメントを参照。
