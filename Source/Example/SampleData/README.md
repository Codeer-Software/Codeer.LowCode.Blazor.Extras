# Example のテスト用データベース

Example (`Source/Example`) のデータは、リポジトリの外にある SQLite のファイルに入っています。
このフォルダの SQL だけで、そのファイルを空の状態から作れます。

- DB のファイル: `C:\Codeer.LowCode.Blazor.Local\Data\sqlite_sample_extras.db`
- 接続文字列を書いてある場所: `Extras/Extras/Extras.Server/appsettings.Development.json` と `Design/designer.settings.Development.json` (どちらも `SampleSQLite`)

## 作り方

リポジトリのルートで、次のコマンドを実行します (.NET 8 SDK が要ります。ほかに入れるものはありません)。

```powershell
dotnet run --project Source/Example/SampleData/Setup
```

- DB のファイルとフォルダが無ければ作ります
- 何度実行しても、今あるデータは消しません。足りない表だけを作ります
- 以前から DB を持っている場合も、同じコマンドで足りない表と列 (`app_users` の `dept` / `role`) が入ります
- 最後に、表の一覧と行数を出します

サンプルのデータを最初の状態に戻したいときは `--reset-samples` を付けます。
下の表で「作り直す」となっている SQL の表だけが作り直されます (自分で足した行は消えます)。

```powershell
dotnet run --project Source/Example/SampleData/Setup -- --reset-samples
```

別の場所に作りたいときは、パスを渡します。

```powershell
dotnet run --project Source/Example/SampleData/Setup -- C:\temp\sample.db
```

## SQL のファイル

上のコマンドは、この順に実行します。

| 順 | ファイル | 作る表 | 今あるデータ |
|---|---|---|---|
| 1 | `base_tables.sql` | 下の「土台の表」のすべて | 消さない |
| 2 | `users_sample.sql` | (表は作らない) `app_users` にユーザー 13 人 | 消さない |
| 3 | `sales_sample.sql` | `customers` / `products` / `sales` | 作り直す |
| 4 | `inquiry_sample.sql` | `inquiries` | 作り直す |
| 5 | `job_trigger_sample.sql` | `job_trigger_samples` | 作り直す |
| 6 | `tag_sample.sql` | `tag_tests` / `tag_test_tags` | 作り直す |

3〜6 は、表がすでにあるときは実行しません (`--reset-samples` を付けたときだけ実行します)。

### 土台の表 (`base_tables.sql`)

| 用途 | 表 |
|---|---|
| ユーザー | `app_users` |
| 監査ログ | `audit_log` |
| アップロード中のファイルの控え | `temporary_files` |
| メールの履歴 | `mail_histories` |
| リッチテキストのメンション | `mention_tests` |
| 編集履歴と、履歴を取る画面 | `edit_histories` / `orders` / `order_items` / `projects` / `project_tasks` / `plans` / `plan_events` / `plan_cards` / `plan_markers` |
| 承認フローと、申請する画面 | `approval_routes` / `approval_route_steps` / `approval_route_step_members` / `approval_flows` / `approval_flow_members` / `approval_histories` / `expense_requests` / `purchase_requests` |
| カレンダー | `schedule` |
| ガントチャート | `task` / `task_dep` |
| カンバンボード | `task_item` |
| 画像マーカー | `marker_item` / `marker2` / `marker_item2` |

`app_users` と `audit_log` が無いと、Example はログインの時点で動きません
(ユーザーを読めない・監査ログを書けないと操作を実行しない設定のため)。

## 手で実行する場合

SQLite のコマンド (`sqlite3`) などで実行するときは、上の表の順に流します。

```powershell
sqlite3 C:\Codeer.LowCode.Blazor.Local\Data\sqlite_sample_extras.db ".read Source/Example/SampleData/base_tables.sql"
```

以前から DB を持っていて `app_users` に `dept` 列や `role` 列が無い場合は、先に無い列の分だけ次の文を 1 回実行します
(`base_tables.sql` は、すでにある表の列を変えません)。

```sql
ALTER TABLE app_users ADD COLUMN dept TEXT;
ALTER TABLE app_users ADD COLUMN role TEXT;
```

## ユーザーのサンプル (`users_sample.sql`)

ログインする人を変えて、権限や候補の違いを確かめるためのデータです。

| 項目 | 値 | 使う場所 |
|---|---|---|
| 部署 (`dept`) | 営業部 5 人 / 開発部 5 人 / 総務部 1 人 / なし 2 人 | メンションの画面の「本文」。@ の候補が同じ部署の人だけになる |
| 役割 (`role`) | 管理者 3 人 (承認 花子・決裁 次郎・総務 みどり) / 一般 9 人 / なし 1 人 | メンションの画面の「管理者だけが読める本文」。役割が管理者の人にだけ見える |

部署と役割は、ユーザーの画面 (サイドバーの「ユーザー」) でも変えられます。

## 表や列を足すとき

設計 (`Source/Example/Design`) に表や列を足したら、このフォルダの SQL も合わせて直します。
DB はリポジトリに入っていないので、SQL に無い表や列は、ほかの人の環境には存在しません。

- 既存の画面が使う表は `base_tables.sql` に `CREATE TABLE IF NOT EXISTS` で足す
- すでにある表に列を足すときは、`Setup/Program.cs` にも「列が無ければ足す」処理を足す (`addedColumns` の一覧に 1 行)
- サンプルの行が要る表は、`〜_sample.sql` を作って `Setup/Program.cs` の一覧に足す
