# MailHistoryContractField (メール履歴契約)

メール送信履歴モジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを
宣言するフィールド。UI もデータも持たない (DB列不要)。

## Design

- どのモジュールが履歴かの指定はサーバー設定 (appsettings の `Mail.HistoryModuleName`)
- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)。
  **契約フィールドが無いモジュールには既定名で書く** (置かなくても従来どおり動く)
- **役割を空にすると「使わない」宣言** (その項目は記録しない)。最小構成 (送信日時 + 件名だけ等) も可
- 役割のフィールドが自モジュールに無ければデザインチェックがエラーにする。リネームに自動追従する

役割: `SentAt` (送信日時・DateTime) / `MailInfraName` / `Subject` / `TotalCount` (数値) /
`SuccessCount` (数値) / `FailureDetails` (失敗明細JSON。Text か Json) / `SourceModule` / `SourceId`
(送信元レコード。MailField / BulkMailField が自動で記録する)。

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MailHistoryContractFieldDesign" }
```

履歴の書き込みはユーザーの書き込み権限に依存しないシステム経路で行われ、
履歴の失敗が送信自体を失敗させることはない。
