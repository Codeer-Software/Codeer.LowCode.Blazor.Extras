# 監査ログ (AuditLog) — サーバーが記録する操作の証跡

「監査ログ」「操作ログ」「アクセスログ」「誰がいつ何をしたかを記録したい」「内部統制 (J-SOX) / ISMS の証跡が要る」と言われたらこれ。
この機能は **Codeer.LowCode.Blazor.Extras.Server のサーバー機能**で、フィールドもスクリプトも要らない。デザイン側の作業は閲覧モジュールとテーブルの生成だけ。

記録するもの (WebAPI ごと・サーバーが自動で書く): ログインの成功 / 失敗 / 二要素認証、レコードの参照・保存・一括取込、ファイル出力・ダウンロード・メール送信、
管理操作、権限による拒否 (401/403)、アプリの起動 / 停止、デザインの版 (App.zip の SHA-256) の切替。
1 行 = いつ・誰 (ユーザー Id)・どこから (IP)・何に (モジュールと行の Id)・何を・結果。**レコードの値は記録しない** (個人情報を複製しない)。
状態を変える操作は、操作の前に「試行」の行、後に「結果」の行の 2 行で残り、試行が書けなければ操作自体を実行しない。

## やってはいけないこと

- **監査ログを自作しない**。ログ用モジュール + `OnDataChanged` 等のスクリプトで記録する作りは、失敗・権限拒否・参照・ログインが残らず、デザインから消せるので証跡にならない
- **論理削除の削除者記録 (`DeletedAt` / `Deleter`) や編集履歴 (EditHistoryField) で代替しない**。編集履歴は「業務データの変更を見て戻す」利用者向けの機能、監査ログは「システムに対して誰が何をしたか (失敗・拒否を含む)」の記録で、目的が違う。両方置くこともある
- **監査ログのテーブルに書き込むモジュールを作らない**。閲覧モジュールからの追加・更新・削除はサーバー (IO インターセプタ) が拒否する

## 分担

| 作業 | 誰が | 内容 |
|---|---|---|
| 閲覧モジュール + テーブル作成 DDL | デザイン (下のセットアップ) | `audit-log-setup` で生成する。手で作らない |
| 記録の有効化 | ホストの appsettings (ユーザー) | `AuditLog` / `AuditLogDatabase` / `AuditLogFile` の 3 セクション。結線はアプリテンプレート (Cookie) に含まれている。デザインからは触れないので、下の「生成後の手順」の文面でユーザーに伝える |
| 追記専用の担保 | DB 管理者 | 監査ログのデータソースを INSERT (閲覧用に SELECT) だけの DB ユーザーで繋ぐ |

## セットアップ (閲覧モジュールとテーブル DDL の生成)

閲覧モジュールは手で JSON を組まず**セットアップで生成する**:

- デザイナ: メニュー Tools > 監査ログのセットアップ
- CLI (headless): `"<デザイナexeのパス>" audit-log-setup "<projectDir>" [--module-name AuditLog] [--table audit_log] [--data-source <name>]
  [--user-module <ユーザーモジュール>] [--user-name-field Name] [--no-pageframe] [--ddl-out "<path.sql>"]`

生成内容:

- 閲覧モジュール (既定名 `AuditLog`): サーバーが書く固定列に対応する 13 フィールド、一覧 / 検索 / 詳細レイアウト、`CanCreate` / `CanUpdate` / `CanDelete` = false、
  誰も書けない `UserWriteCondition` (保護条件)。日時 (`OccurredAt`) は UTC で入っているので `SaveAsUtc: true` で表示をローカル時刻に直す。
  分類 (`Category`) と結果 (`Result`) は候補付きの SelectField。操作者 (`UserId`) はユーザーモジュールへの LinkField (表示は `--user-name-field`)
- PageFrame のページリンク「監査ログ」(新規作成なし・行から詳細へ遷移・Id の降順)。`--no-pageframe` で作らない
- テーブル作成 DDL (`create table` + 日時 `occurred_at_utc` のインデックス)。**テーブルの列はサーバーが決める**ので、モジュールからではなくこの DDL でテーブルを作る

**冪等**: 同名モジュールがあれば生成しない。テーブルが既にあれば DDL を出さない。
既定: `--data-source` は先頭のデータソース、`--user-module` はアプリ設定 (app.clprj) のカレントユーザーモジュール (未設定なら `AppUser`)。
**DDL は自動実行されない**。`--ddl-out` に書き出し、`sql` CLI で流してテーブルを作る (流したら `designcheck` で確認)。
`--table` / `--data-source` は、ホストの appsettings の `AuditLogDatabase` (`Table` / `DataSourceName`) と同じにする。

## 生成後の手順

1. **閲覧できる人を絞る**: 生成直後は誰でも読める。閲覧モジュールの `UserReadCondition` を監査役・管理者に限定する (条件に使うフィールドはユーザーモジュールの実フィールドに合わせる)

   ```json
   "UserReadCondition": {
     "ModuleName": "AppUser",
     "Condition": {
       "SearchTargetVariable": "Role.Value",
       "Comparison": "Equal",
       "Value": { "Value": "Admin", "TypeFullName": "Codeer.LowCode.Blazor.Repository.StringValue" },
       "TypeFullName": "Codeer.LowCode.Blazor.Repository.Match.FieldValueMatchCondition"
     }
   }
   ```

2. **記録の有効化をユーザーに伝える** (ホストの appsettings。デザインの作業ではない)。テーブル名とデータソース名はセットアップに渡したものと同じにする

   ```json
   "AuditLog": {
     "Enabled": true,
     "FailureMode": "Strict",
     "RetentionDays": 1825,
     "Categories": [ "Authentication", "DataWrite", "Export", "Admin", "System" ]
   },
   "AuditLogDatabase": { "DataSourceName": "Main", "Table": "audit_log" }
   ```

   `Categories` は成功した操作の絞り込みで、失敗と拒否は常に記録される。参照の証跡まで要るなら `DataRead` を足す (行ごとに残るので量が多い)。
   ファイルにも出すなら `"AuditLogFile": { "Directory": "<フォルダ>" }` を足す (SIEM への転送元)
3. **追記専用の担保を伝える**: 監査ログのデータソースは INSERT (と SELECT) だけの DB ユーザーで繋ぎ、厳密に求められる環境では `RetentionDays` を 0 にして掃除は DB 管理者のジョブで行う
4. `designcheck` を実行する

## 生成物の構成 (生成後に直すときに崩さない)

- `DbColumn` は固定 (サーバーが書く列)。変えない・列を足さない。表示名・ラベル・一覧に出す列・並び・検索項目の調整は自由
- `CanCreate` / `CanUpdate` / `CanDelete` は false のまま、`UserWriteCondition` の保護条件は残す
- 対象 (`Targets`) と詳細 (`Detail`) は長い文字列。検索は LIKE 相当になる。対象は行ごとの JSON で `{"Module":"Order","Id":"123","Operation":"Update"}` の形なので、
  「あるレコードを触った操作」は `Targets` を `{"Module":"Order","Id":"123",` で部分一致検索する

## 閲覧モジュールの列

| フィールド | 列 | 内容 |
|---|---|---|
| `OccurredAt` | `occurred_at_utc` | 発生時刻 (UTC)。試行の行は操作の前、結果の行は操作の後 |
| `Category` | `category` | 分類: `Authentication` / `DataRead` / `DataWrite` / `Export` / `Admin` / `System` / `Other` |
| `Action` | `action` | 操作の名前 (`ModuleData.Submit`、`Account.Login` など) |
| `Result` | `result` | `Attempt` (試行) / `Success` / `Failure` / `Denied` (権限・認証の拒否) / `Continued` (対象が 500 件を超えたときの続きの行) |
| `UserId` | `user_id` | 操作したユーザー (ユーザーモジュールの行の Id)。未認証なら空 |
| `ClientIp` / `UserAgent` | `client_ip` / `user_agent` | 接続元 |
| `RequestId` | `request_id` | リクエストの識別子。試行と結果の 2 行、続きの行を結ぶ鍵 |
| `Host` | `host` | 記録したサーバー名 |
| `DesignVersion` | `design_version` | その操作が使ったデザインの版 (App.zip の SHA-256) |
| `Targets` | `targets` | 対象のレコード (モジュール・Id・操作) の並び |
| `Detail` | `detail` | 件数 (`Add=3; Update=1; Delete=0`)、取込ファイルのハッシュ、失敗の理由、試行したログイン名など |

仕様の全文 (分類ごとに何が記録されるか・デザインの版・出力先・保持と削除・改ざん対策・ホストの結線) は Extras の
[docs/AuditLog.md](https://github.com/Codeer-Software/Codeer.LowCode.Blazor.Extras/blob/main/docs/AuditLog.md)。
