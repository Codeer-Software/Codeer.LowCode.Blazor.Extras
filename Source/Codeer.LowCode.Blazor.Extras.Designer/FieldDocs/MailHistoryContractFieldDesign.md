# MailHistoryContractField (メール履歴契約)

メール送信履歴モジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを
宣言するフィールド。UI もデータも持たない (DB列不要)。

## セットアップ (履歴モジュールの自動生成)

履歴モジュールは手で作らず**セットアップコマンドで生成できる** (契約フィールド・保護条件・一覧同梱):

- デザイナ: メニュー Tools > メール履歴モジュールの生成
- CLI (headless): `<designer.exe> mail-history-setup "<projectDir>" [--name MailHistory] [--data-source <name>] [--ddl-out <path.sql>]`

生成後、サーバーの appsettings に `"Mail": { "HistoryModuleName": "<モジュール名>" }` を設定し、
DDL でテーブルを作成すると全送信が自動記録される。

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
