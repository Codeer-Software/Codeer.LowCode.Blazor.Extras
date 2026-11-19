# Example のサンプルデータ

Example のデータは、リポジトリの外にある SQLite のファイル (`C:\Codeer.LowCode.Blazor.Local\Data\sqlite_sample_extras.db`) に入っています。
ここにある SQL は、そのファイルに対して実行します。

## 必ず実行するもの

| ファイル | 内容 |
|---|---|
| `mention_sample.sql` | ユーザーの表 (`app_users`) に部署の列 (`dept`) を足し、部署の違う人を増やす |

**`mention_sample.sql` を実行していないと、Example は動きません。**
`AppUser` モジュールの `Dept` フィールドがこの列を読むので、列が無いとユーザーを読む処理 (ログイン中のユーザーの読み込みを含む) がすべてエラーになります。

- 先頭の `ALTER TABLE` は 1 回だけ実行します (2 回目は「列がすでにある」のエラーになります)
- 2 行目以降は何度実行しても同じ結果になります

## 画面を試すときに実行するもの

どれも新しい表を作るだけで、作り直せます (DROP → CREATE → INSERT)。実行していなくても、ほかの画面は動きます。

| ファイル | 試す画面 |
|---|---|
| `inquiry_sample.sql` | AI チャット (問い合わせ) |
| `sales_sample.sql` | AI チャット (売上の集計) |
| `job_trigger_sample.sql` | ジョブトリガー |
| `tag_sample.sql` | タグ (TagTest) |
