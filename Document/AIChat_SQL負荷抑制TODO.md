# AIChat (RawDataAccessAgent) の SQL 負荷抑制 — 現状と TODO

2026-09-10 時点の整理。AIChatField の `RawDataAccess` Agent が DB に重い SQL を投げないための工夫は
「結果を返す側」の歯止めだけで、「DB に重い SQL を投げさせない側」はほぼ無い。実装はまだ。

対象コード: `Source/Codeer.LowCode.Blazor.Extras.Server/AI/Chat/RawDataAccess/`
(`RawDataAccessOptions.cs` / `RawDataAccessToolSet.cs` / `DbSchemaReader.cs`)

## 現状入っているもの

| 項目 | 場所 | 内容 |
|---|---|---|
| 行数上限 | `RawDataAccessOptions.MaxRows` (既定 200) | reader を MaxRows 行で打ち切り `truncated=true` を AI に返す。**DB 側の実行は止まらない** |
| 文字数上限 | `MaxResultChars` (既定 20000) | 結果 JSON が超えたら行を半分ずつ捨てる (トークンの歯止め) |
| タイムアウト | `CommandTimeoutSeconds` (既定 30) | `DbCommand.CommandTimeout` |
| SELECT 限定 | `RawDataAccessToolSet.Validate` | 1 文・SELECT/WITH 始まり・禁止語 (INSERT/UPDATE/DELETE 等) の簡易判定。本体の防御は読み取り専用 DB ユーザー |
| プロンプト誘導 | `RawDataAccessToolSet` のツール説明 | 「最大 N 行しか返らない、生の行より GROUP BY/SUM/COUNT」「get_schema は目次→必要な表の列だけ」、方言ごとの TOP/LIMIT/FETCH FIRST の書き方 (`DbSchemaReader.DialectName`) |
| 往復回数 | `MaxToolCallRoundsPerReply` (既定 10) | 1 返事あたりのツール呼び出し上限 |
| スキーマキャッシュ | `SchemaCacheDuration` (既定 10 分) | INFORMATION_SCHEMA の再読込抑制 |
| ログ | `RawDataAccessToolSet` | 実行 SQL をユーザー名・会話 ID 付きで Information ログ、拒否は Warning |

テンプレ (Starter Cookie `AI/AIChatAgentTable.cs`) は `RawDataAccessOptions { DataSourceNames = ... }` しか渡していないので、
上の数値はすべて既定値のまま。appsettings から変えられない。

## 入っていないもの

- **LIMIT / TOP の自動付与が無い**。AI が付け忘れると DB はフルスキャン・全件ソートを実行し、こちらは先頭 200 行で読むのをやめるだけ
  (ORDER BY 付きなら DB の負荷は変わらない)。
- **テーブル規模の把握が無い**。`DbSchemaReader` は列情報 (表名/列名/型) だけで、行数や統計を見ていない。AI は「この表は 1000 万行」と知らずに書く。
- 実行前のコスト見積もり (EXPLAIN) 無し。
- 同時実行数の制限 (ユーザー単位 / プロセス単位) 無し。1 会話あたりの SQL 本数制限も無し (往復 10 回 × 会話ターン数)。
- SQL の結果キャッシュ無し (同じ質問で毎回 DB に行く)。

## TODO (効きそうな順)

1. **LIMIT/TOP の自動被せ**
   - `Validate` を通った SELECT を方言別にサブクエリ化して `MaxRows + 1` 行で切る:
     SQL Server `SELECT TOP (n) * FROM (...) AS q`、PG/MySQL/SQLite `SELECT * FROM (...) AS q LIMIT n`、Oracle `... FETCH FIRST n ROWS ONLY`。
   - AI が自分で TOP/LIMIT を書いていても二重に被せて問題ない (外側は上限の保険)。
   - 注意: 外側で被せても ORDER BY 付きの全件ソートは消えない。集計 (GROUP BY) も全件走る。「読む行数」ではなく「DB の仕事量」を減らすには 2・3 が要る。
   - 副次効果: reader 打ち切りではなく DB 側で行数が確定するので `truncated` 判定は「n+1 行目が来たか」に変える。
2. **概算行数をスキーマに添える**
   - `get_schema` の目次 (表名一覧) に概算行数を出し、プロンプトで「大きい表 (例 100 万行以上) は必ず WHERE で期間等を絞る / 集計する」と誘導。
   - 取り方 (統計ベース・軽い): SQL Server `sys.partitions` (index_id 0/1 の rows 合計)、PostgreSQL `pg_class.reltuples`、MySQL `information_schema.tables.table_rows`、
     Oracle `USER_TABLES.NUM_ROWS`、SQLite は無い (COUNT(*) は避ける。省略)。
   - `DbSchemaReader.Column` に表ごとの行数を持たせる別クエリを足し、`SchemaCacheDuration` で一緒にキャッシュ。
   - 読み取り専用 DB ユーザーでも上のカタログは読めるかを各 DB で確認する (PG の `pg_class` は公開、SQL Server の `sys.partitions` は VIEW DEFINITION が要る場合あり → 取れなければ「不明」で出す)。
3. **タイムアウトを短く・設定に出す**
   - 既定 30 秒は長い。10 秒程度に下げる案。切れたときは AI に「時間切れ。条件で絞るか集計にせよ」とエラー文で返し再試行させる (今も例外文がそのまま返る)。
   - `MaxRows` / `CommandTimeoutSeconds` / `MaxResultChars` をテンプレの `AIChatSettings` (appsettings `AIChat`) に出して `AIChatAgentTable` から渡す。
4. **同時実行の制限**
   - `RawDataAccessToolSet` に `SemaphoreSlim` (プロセス全体 N 本、既定 2〜4) を置き、超えたら待つか「混んでいる」で返す。
   - ユーザー単位の連続実行数 (1 会話ターンあたりの SQL 本数) も `MaxToolCallRoundsPerReply` とは別に持つか検討。
5. **(任意) EXPLAIN によるコスト門番**
   - PG なら `EXPLAIN (FORMAT JSON)` の `Total Cost` / `Plan Rows` で閾値超えを拒否できる。SQL Server は `SET SHOWPLAN_XML ON` で取れるが読み取り専用ユーザーで動くかは要確認。方言差が大きいので 1〜4 の後。

## 検証の観点

- Extras.Test の AIChat テスト (`RawDataAccessToolSet.Validate` の単体、SQLite での execute_sql) に、LIMIT 被せの方言別 SQL 生成と行数打ち切りのケースを足す。
- 実 DB での確認: Manual の LowCodeSamples デモ (PG) と、Codeer.Internal.System (SQL Server) の AIChat で、行数の多い表に対して「全件ください」と聞いて DB 側のクエリ時間をログで見る。
