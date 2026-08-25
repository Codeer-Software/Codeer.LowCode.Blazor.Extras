# MailHistoryContractField (メール履歴契約)

メール送信履歴モジュールに1つ置き、「役割 → 自モジュールのフィールド名」のマッピングを
宣言するフィールド。UI もデータも持たない (DB列不要)。

## セットアップ (メールのセットアップで自動生成)

履歴モジュールは手で作らず**メールのセットアップで生成できる** (契約フィールド・保護条件・一覧・ページリンク同梱。
同時にユーザーモジュールの差出人契約や Gmail トークン欄、サーバー設定の案内も揃う):

- デザイナ: メニュー Tools > メールのセットアップ
- CLI (headless): `<designer.exe> mail-setup "<projectDir>" [--history-name MailHistory] [--data-source <name>]
  [--user-module AppUser] [--user-email-field Email] [--user-name-field Name] [--no-sender-contract] [--gmail-token]
  [--no-history] [--infra Smtp|GraphApi|SendGrid|Gmail] [--no-pageframe] [--ddl-out <path.sql>]`

生成後、案内どおりサーバーの appsettings に `"Mail": { "HistoryModuleName": "<モジュール名>" }` を設定し、
DDL でテーブルを作成すると全送信が自動記録される。承認フローのセットアップで「メールを使う」を選んだ場合も同じものが生成される。

## Design

- どのモジュールが履歴かの指定はサーバー設定 (appsettings の `Mail.HistoryModuleName`)
- 各プロパティ (役割) の初期値は既定フィールド名。既定名でフィールドを作れば設定不要 (置くだけ)。
  **契約フィールドが無いモジュールには既定名で書く** (置かなくても動く)
- **必須は送信日時だけ** (デザイナの表示名に「(必須)」が付く)。**他の役割は空にすると「使わない」宣言**
  (その項目は記録しない)。契約フィールドを置かない場合も、必須以外は既定名のフィールドが無ければ記録しないだけ
- 必須役割が空・契約が名指ししたフィールドが不在ならデザインチェックがエラーにする。リネームに自動追従する
- **履歴を取る設定 (`Mail.HistoryModuleName`) なのに上記を満たしていない場合は、送信前に実行時エラー**
  になって**メールを送らない** (記録が静かに欠けるのを防ぐ。履歴モジュールは appsettings 指定なので
  デザインチェックからは辿れず、実行時に検出するしかない)

| 役割 (表示名) | 内容 | 必須 |
|---|---|---|
| SentAt (送信日時 (必須)) | 送信操作の日時 (DateTime) | ○ |
| MailInfraName (メールインフラ名) | 送信先の呼び名 | - |
| Subject (件名) | 件名 | - |
| TotalCount (送信対象数) | 数値 | - |
| SuccessCount (成功数) | 数値 | - |
| FailureDetails (失敗明細) | 失敗明細 JSON (Text か Json) | - |
| SourceModule (送信元モジュール) | 送信元レコードのモジュール名 | - |
| SourceId (送信元Id) | 送信元レコードの Id | - |

```json
{ "Name": "Contract", "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.MailHistoryContractFieldDesign" }
```

履歴の書き込みはユーザーの書き込み権限に依存しないシステム経路で行われ、
履歴の失敗が送信自体を失敗させることはない。
