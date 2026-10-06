# AIChat (RawDataAccessAgent) の SQL 負荷抑制 — 実装仕様

## 実装結果 (2026-10-06・Extras.Server 0.17.3)

下の仕様どおり実装済み。単体テストは AI フォルダ 118 本すべて緑。実 DB の確認は `Test/AI/RawDataAccessLoadRealDbTest` ([Explicit]・環境変数 `RAWDATA_LOAD_MSSQL_CONNECTION` / `RAWDATA_LOAD_PG_CONNECTION`) で行った。

| 確認 | SQL Server 2022 (SELECT だけを GRANT したログイン・200 万行) | PostgreSQL (デモの `ai_chat_reader`) |
|---|---|---|
| 統計 (行数・索引の先頭列) が読めるか | 読める (2,000,000 行・`id, created, customer_id`)。`sys.partitions` / `sys.indexes` は表に SELECT があれば見える | 読める (未解析の表は `reltuples = -1` → 不明として扱う) |
| 行数制限なしの `SELECT *` | 中止あり 18〜22 ms / 中止なし 1,339〜1,476 ms (ドライバが残りの行を読み捨てることを実測で確認) | 未計測 (Docker が起動せず、大きい表を作れなかった) |
| 重い集計 (2 秒のタイムアウト) | 2.5 秒で「時間切れ」を返し、DB 側の実行中クエリは 0 件 | 未計測 |

カタログ SQL は下書きのまま変更なし。MySQL / Oracle のカタログ SQL は実 DB で未確認。

**既定値の訂正 (同日)**: 「待たせる・断る種類の上限は既定で無効」に変更 (バージョンを上げただけの既存アプリの動きを変えない)。`CommandTimeoutSeconds` 30 (従来どおり) / `MaxQuerySecondsPerReply` 0 / `MaxConcurrentQueries` 0 / `CancelQueryAtRowLimit` true / `LargeTableRows` 100000。`IncludeIndexColumns` は削除 (索引は `LargeTableRows` と一緒に読む。読めなければ省く)。下の仕様の表と appsettings 例はこの訂正で読み替える。

---

2026-10-06 確定。狙いは「AI が書いた SQL で本番 DB が重くなり業務画面が遅くなる」ことの抑制 (DB の負荷)。
Web サーバーのメモリは現状でも 200 行・2 万文字で切っているので対象外。

重い SQL を事前に見分けることはしない。**1 本あたりの時間・1 返事あたりの時間・同時に走る本数に上限を付け**、
行数と索引の情報で AI を軽い SQL へ誘導する。全部 appsettings で数値変更と無効化ができる。

## 対象と範囲

| リポ | 変更 |
|---|---|
| Extras (`Source/Codeer.LowCode.Blazor.Extras.Server/AI/Chat/RawDataAccess/`) | 本体。`RawDataAccessOptions.cs` / `RawDataAccessToolSet.cs` / `DbSchemaReader.cs` + 新規 internal クラス 2 つ |
| Extras (`Source/Codeer.LowCode.Blazor.Extras.Test/AI/`) | テスト |
| Extras (`docs/AIChatField.md`・`CLAUDE.md`・Example の `AIChatSettings` / `AIChatAgentTable` / appsettings) | 文書と Example 結線 |
| Starter (`Source/Hosts/Cookie/LowCodeApp.Server/AI/AIChatSettings.cs`・`AIChatAgentTable.cs`・`appsettings.json`・`CLAUDE.md`) | appsettings から渡す結線 |

触らないもの: 本体 (core)・Extras.Designer (FieldDocs)・Extras クライアント・ModuleDataAccess (feature ブランチ)。
VSIX 再生成・tfs `Source/App` への export-debug・デモ / 社内システムへの反映・コミットは、この作業に含めない (指示待ち)。

## 設定 (RawDataAccessOptions に追加)

`0` または `false` で、その対策を無効にできる。

| プロパティ | 既定 | 内容 | 無効化 |
|---|---|---|---|
| `CancelQueryAtRowLimit` (bool・新規) | `true` | `MaxRows` を超えたら DB にクエリの中止を送る | `false` |
| `CommandTimeoutSeconds` (既存) | **30 → 15** | SQL 1 文のタイムアウト (秒) | `0` (無制限) |
| `MaxQuerySecondsPerReply` (int・新規) | `45` | 1 回の返事で SQL の実行に使える合計時間 (秒) | `0` (無制限) |
| `MaxConcurrentQueries` (int・新規) | `2` | 同じデータソースへ同時に実行する SQL の本数 | `0` (無制限) |
| `LargeTableRows` (int・新規) | `100000` | この行数以上の表を「大きい表」として AI に伝える | `0` (行数も索引も読まない) |
| `IncludeIndexColumns` (bool・新規) | `true` | 大きい表に索引の先頭列を添える | `false` |

XML コメントは既存の書き方 (1 行・日本語) に合わせる。`CommandTimeoutSeconds` のコメントに「0 で無制限」を足す。

## 実装

### 1. 行数打ち切りで DB のクエリを中止する (`ExecuteSqlAsync`)

SQL は書き換えない (サブクエリで LIMIT/TOP を被せる案は不採用: SQL Server は派生表の ORDER BY と WITH の入れ子がエラー、
MySQL は派生表の ORDER BY を捨てる、列名の重複は全 DB でエラー)。

- `MaxRows` 行まで読む。その次の `ReadAsync` が true なら `truncated = true`。
- `truncated` かつ `CancelQueryAtRowLimit` なら `command.Cancel()` を呼んでから reader を閉じる
  (SqlClient / Npgsql は Cancel なしの Dispose で残りの行を全部読み捨てる = DB は全件を送り切る)。
- Cancel 後は reader / command / `IDbAccessor` の後始末で例外が出ることがある (PG 57014、SqlClient「Operation cancelled by user」等)。
  **打ち切った場合に限り**、後始末の例外は握りつぶして、読めた行を結果として返す。`using` / `await using` をやめて try/finally で書く。
  打ち切っていない場合の例外は今までどおり error として返す。
- `DbAccessor` は接続ごとにトランザクションを持つことがある (`GetTransaction`)。Cancel 後の `DisposeAsync` がどう動くかを実装時に確認する。

効くのは行をそのまま返す型のクエリ (SELECT * や索引順)。集計・索引のない ORDER BY・大きい JOIN には効かない (2〜4 が受け持つ)。

### 2. 時間予算

新規 internal クラス `QueryTimeBudget` (同じフォルダ)。1 回の返事の間だけ生きる。置き場は `context.Items` (キーは定数)。

- `Remaining` (TimeSpan? 。無制限なら null) / `Add(TimeSpan elapsed)`。スレッドセーフにする (lock)。
- `ExecuteSqlAsync` の手順:
  1. 検証 (`Validate`) を通った後、予算を見る。残りが 0 以下なら **DB へ行かずに** error を返す:
     `この返事で SQL に使える時間 (N 秒) を使い切りました。これ以上 SQL を実行せず、ここまでの結果で答えてください。`
  2. その SQL のタイムアウト = `CommandTimeoutSeconds` と残り予算 (秒・切り上げ・最小 1) の小さいほう。片方が無制限ならもう片方。両方無制限なら 0。
  3. 実行時間は Stopwatch で測り (同時実行ゲートの待ち時間は含めない)、成功・失敗どちらでも予算に足す。
- 時間切れの判定: 例外が出て、タイムアウトが 0 でなく、経過時間がタイムアウトの 0.5 秒手前以上。そのときの error:
  `N 秒で時間切れになりました。同じ SQL を繰り返さず、期間などの条件で絞るか、集計の範囲を狭めるか、行数制限を付けてください。(元のエラー: …)`
- SQLite は時間で止まらない (Microsoft.Data.Sqlite の CommandTimeout はロック待ちだけ)。割り切りとして docs に書く。

### 3. 同時実行ゲート

新規 internal クラス `QueryGate` (同じフォルダ)。

- static な `ConcurrentDictionary<string, SemaphoreSlim>`。キーは `データソース名 (小文字化) + "|" + MaxConcurrentQueries`。
  プロセス全体で共有する (Starter の対応表は `""` と `"RawDataAccess"` で Agent を 2 つ作るので、インスタンス単位だと上限が倍になる)。
  上限値をキーに入れるのは、設定の違う Agent やテストどうしが干渉しないため。
- `MaxConcurrentQueries <= 0` ならゲートなし。
- 待ち: `WaitAsync(待ち上限, context.CancellationToken)`。待ち上限は `CommandTimeoutSeconds` 秒 (0 なら上限なしで待つ)。
  取れなければ error: `データベースが混み合っています。少し待ってからもう一度試してください。`
- 解放は finally。スキーマ読み込み (`LoadSchemaAsync`) はゲートの対象外。

### 4. 大きい表と索引を AI に伝える

`DbSchemaReader` に追加:

```csharp
public sealed record TableStats(long? Rows, IReadOnlyList<string> IndexColumns);
public static Task<Dictionary<string, TableStats>> ReadTableStatsAsync(IDbAccessor db, string dataSourceName, int timeoutSeconds, bool includeIndexColumns, CancellationToken cancellationToken)
```

- キーは `ReadAsync` の `Column.Table` と同じ形 (SQL Server は `schema.table`、PG は public なら表名だけ、他は表名)。大文字小文字を区別しない。
- **行数と索引は別々に読み、どちらも失敗したら空で返す** (例外にしない。Warning ログ)。権限で読めない DB でも SQL 実行は動く。
- `IndexColumns` は各索引の先頭列 (重複なし)。
- カタログ SQL (下書き。実 DB で確かめて直す):

| DB | 行数 | 索引の先頭列 |
|---|---|---|
| SQL Server | `SELECT s.name + '.' + t.name, SUM(p.rows) FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0, 1) GROUP BY s.name, t.name` | `sys.indexes` (type 1, 2・is_disabled = 0) + `sys.index_columns` (key_ordinal = 1) + `sys.columns` |
| PostgreSQL | `pg_class` (relkind r / p / m) の `reltuples::bigint`。負の値は不明。スキーマ名の付け方は `ReadAsync` と同じ CASE | `pg_index` (indisvalid・`indkey[0] <> 0`) + `pg_attribute` (attnum = `indkey[0]`) |
| MySQL | `information_schema.tables` の `table_rows` (table_schema = DATABASE()・BASE TABLE) | `information_schema.statistics` (seq_in_index = 1) |
| Oracle | `USER_TABLES.NUM_ROWS` | `USER_IND_COLUMNS` (COLUMN_POSITION = 1) |
| SQLite | 読まない | 読まない |

`RawDataAccessToolSet`:

- `_schemaCache` を「列 + 統計」の組にして、`LoadSchemaAsync` で一緒に読み、同じ `SchemaCacheDuration` でキャッシュする。
  `LargeTableRows <= 0` なら統計は読まない。`IncludeIndexColumns == false` なら索引は読まない。
- `get_schema` の目次: `- sales (12 列, 約 12,000,000 行) [モジュール Sales]`。行数が不明なら今までどおり。
  表指定のときは、行数と索引が分かる表だけ表名の後ろに `{約 12,000,000 行; 索引: sale_date, customer_id}` を添える。
- `GetInstructions` (システムプロンプト): `LargeTableRows` 以上の表を行数の多い順に最大 30 件。`ExcludedTables` は除く。該当が無ければ節ごと出さない。

  ```
  - 大きい表 (必ず期間などの条件で絞るか、絞った上で集計してください。条件は索引のある列に、列を関数で包まず範囲で書きます):
    - Main: sales 約 12,000,000 行 (索引: sale_date, customer_id)
  ```

  `GetInstructions` は同期なので、`Dialects()` と同じくここでだけ同期で待つ。**読み込みに失敗してもこの節を省くだけ**で、返事は止めない (try/catch)。
  プロンプトは「get_schema はなるべく呼ばない」と誘導しているので、目次だけに出しても AI の目に入らない。だからプロンプトにも出す。

ビュー経由 (監査方針で勧めているマスク済みビュー) は行数が取れない。大きいビューは補足文書に書いてもらう (docs に記載)。

### 5. プロンプトとログ

`GetInstructions` に追加:

- `集計でない SELECT (行をそのまま返すもの) には必ず行数制限を付け、必要な列だけを選んでください (SELECT * は避ける)。`
- タイムアウトが 0 でないとき: `1 つの SQL は N 秒で打ち切られます。時間切れになったら同じ SQL を繰り返さず、条件で絞るか集計の範囲を狭めてください。`

ログ (`context.Logger`):

- 成功: Information `AIChat RawDataAccess SQL done by {User} on {DataSource}: {ElapsedMs} ms, rows={Rows}, truncated={Truncated}`
- 失敗: 既存の Warning に `{ElapsedMs}` と `timedOut={TimedOut}` を足す
- ゲート待ち切れ・予算切れ: Warning

モデルに返す文言は既存どおり日本語の定数 (resx にしない)。

## ホストの結線 (appsettings)

`RawDataAccessOptions` は appsettings からそのまま束縛できる作りなので、セクションを 1 つ足すだけにする。

Starter `AI/AIChatSettings.cs` と Extras Example の同ファイル:

```csharp
/// <summary>RawDataAccess Agent の上限値 (SQL のタイムアウト・同時実行数など。0 / false で無効)。</summary>
public RawDataAccessOptions RawDataAccess { get; set; } = new();
```

`AI/AIChatAgentTable.cs`: `new RawDataAccessOptions { DataSourceNames = … }` を次に置き換える。

```csharp
var options = config.AIChat.RawDataAccess;
options.DataSourceNames = dataSourceNames;
```

`appsettings.json` (Starter Cookie・Example の Development。Example の `appsettings.AIChatTest.json` は gitignore のローカルファイルなので同じキーを足すだけで中身は読まない):

```json
"AIChat": {
  "RawDataAccessDataSources": [],
  "RawDataAccess": {
    "CommandTimeoutSeconds": 15,
    "MaxQuerySecondsPerReply": 45,
    "MaxConcurrentQueries": 2,
    "CancelQueryAtRowLimit": true,
    "LargeTableRows": 100000,
    "IncludeIndexColumns": true
  }
}
```

セクションを書いていない既存アプリは既定値で動く (対応表の 2 行を直さなくてもコンパイルは通る。破壊的変更なし)。
Starter `CLAUDE.md` の appsettings 表 (`AIChat` の行) に `RawDataAccess` の説明を足す。テンプレのコメントは「触るときの手がかり」だけにする。

## テスト (Extras.Test/AI)

`RawDataAccessToolSetTest` (SQLite) に追加:

- 無限に行を返す再帰 CTE (`WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c) SELECT x FROM c`) が `MaxRows` 行・`truncated=true` ですぐ返る (`CancelQueryAtRowLimit` true / false の両方)
- ちょうど `MaxRows` 行の結果は `truncated=false`
- 予算切れ: 予算を使い切った状態の `QueryTimeBudget` を `context.Items` に置くと、DB へ行かず error が返る
- `MaxQuerySecondsPerReply = 0`・`MaxConcurrentQueries = 0`・`LargeTableRows = 0` で従来どおり動く
- 統計つきの目次・表指定・プロンプトの書式 (統計は internal の差し込み口からテスト用の値を入れる。SQLite は統計を読まないため)
- 統計の読み込みが例外でも `get_schema` と `GetInstructions` が動く

新規テスト:

- `QueryTimeBudgetTest`: 残りの計算・タイムアウトの丸め (切り上げ・最小 1・無制限の組み合わせ)
- `QueryGateTest`: 上限まで取れる・超えると待つ・待ち切れで false・解放で次が通る・同じキーは同じゲート・上限 0 はゲートなし

既存の AI テストが緑のままであること。`RawDataAccessAgentRealAITest` ([Explicit]) は任意。

## 実 DB での確認

- PG (Manual の LowCodeSamples デモ・`ai_chat_reader`) と SQL Server (Codeer.Internal.System) で、カタログ SQL が AI 用の読み取り専用ユーザーで読めるか
  (特に SQL Server の `sys.partitions` / `sys.indexes`)。読めなければ「不明」で動くことも見る。
- 行数の多い表に `SELECT *` (行数制限なし) を投げ、`CancelQueryAtRowLimit` true / false で DB 側の実行時間を比べる
  (「Dispose が残りを読み捨てる」はドライバの仕様からの判断で未実測。ここで確かめる)。
- 重い集計が `CommandTimeoutSeconds` で DB 側でも止まること (実行中クエリの一覧で確認)。
- 秘密情報 (接続文字列・キー) はファイルの中身を表示しない・リポジトリに書かない。

## 文書

- `docs/AIChatField.md`: 「RawDataAccessAgent と DB の権限」の後に「DB の負荷を抑える」節。
  - ライブラリが保証すること: データソースごとに同時 N 本・1 本 T 秒・1 返事 S 秒。設定の表と appsettings の例
  - 効く範囲: 行数打ち切りは行をそのまま返すクエリに効く。集計には時間と本数の上限で対応
  - DB 側でやること: AI 用データソースを読み取りレプリカに向ける (本番の負荷をなくす唯一の方法)・AI 用ロールに DB 側のタイムアウトと接続数上限・集計済みの表やビューを見せる
  - 割り切り: SQLite は時間で止まらない・スケールアウト時は上限がインスタンスごと・ビューは行数が出ないので補足文書に書く
- `CLAUDE.md` (Extras): AIChat の節に 1〜2 行。
- この文書: 実装後、結果 (実測値・直したカタログ SQL) に合わせて書き換える。

## 版と pack

- nuget.org の Extras.Server は 0.17.2 まで公開済み。localnuget の 0.18.0 は feature ブランチ (ModuleDataAccess) の未公開版。
  **この作業は main で Extras.Server 0.17.3** にする (Extras / Extras.Designer は無変更なので版を上げない)。
- pack 前に現在ブランチが main であることを確認する。
- `PackageReleaseNotes` は書き換え (英語 1 行): `AIChat RawDataAccess: query load limits (cancel at row limit, time budget, concurrency limit, large-table hints).`
- pack → `C:\localnuget` と `C:\nubk`。NuGet キャッシュは `C:\bin\nuget-packages`。
- Starter Hosts の Extras.Server 参照を 0.17.3 に上げ、Cookie の Server を別 OutDir でビルドして確認する。

## やらないもの

| 項目 | 理由 |
|---|---|
| LIMIT / TOP の自動被せ | SQL の書き換えは方言ごとに壊れる (上記)。行数打ち切り + Cancel で同じ効果 |
| EXPLAIN によるコスト門番 | コストの単位が DB ごとに違い閾値を決められない。統計が古いと誤拒否。SQL Server は SHOWPLAN 権限が要る。上限は時間と本数で保証済み |
| SQL の結果キャッシュ | AI の書く SQL は毎回文面が変わり当たらない |
| ユーザー単位の同時実行制限 | AI 利用者どうしの公平性の話。DB の保護は全体のゲートで足りる |
