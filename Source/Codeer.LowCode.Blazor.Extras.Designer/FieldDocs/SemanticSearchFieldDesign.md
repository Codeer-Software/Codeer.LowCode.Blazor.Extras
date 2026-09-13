## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

モジュールの行を **内容の意味で探せる** ようにする書き込み専用フィールド (UI なし)。Submit のたびに `SourceFields` の値を「表示名: 値」の 1 行ずつに並べた文章を作って送り、サーバーがその文章の埋め込みベクトル (embedding) を付けて、2 つの書き込み専用 DB カラム (`DbColumnText` / `DbColumnVector`) に保存する。両カラムは読み戻されない。

AI チャット ([AIChatField](AIChatFieldDesign.md) の `RawDataAccessAgent`) はこの列を `search_records` ツールで使い、「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える (件数・合計などの集計は SQL のまま)。

> [PasswordHashField](PasswordHashFieldDesign.md) と同型の「入力欄を持たず、保存の裏で列を書く」フィールド。レイアウトに置く必要はないが `Fields` には必ず含める。

### ⚠ サーバ側の実装が必須（重要）

このフィールドを置くだけでは**ベクトルは付かない**。埋め込みモデルの用意と保存時の索引付けはホストアプリの責務で、Extras.Server の `Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch.SemanticSearchIndexer` を使う。

- 保存時: `ModuleDataIO` の派生 (通常 `CustomizedModuleDataIO.AddAsync` / `UpdateAsync`) で `await indexer.ApplyAsync(designData, data, isNewData)` を呼ぶ。送られてきた文章に埋め込みを付ける (新規で文章が無ければサーバーで組み立てる = 一括取込)。埋め込みモデル未設定・失敗のときは文章だけ保存しベクトルは NULL (警告ログ)
- AI チャット: `RawDataAccessAgent` のコンストラクタ `embeddingGeneratorFactory` に埋め込みモデルを渡す。渡したときだけ `search_records` が付く
- 再索引: `SemanticSearchIndexer.ReindexAsync(moduleDataIO, designData, moduleName)` で全行を通常 Submit で書き直す (フィールドを後から置いたとき・モデルを変えたとき)
- 埋め込みモデルは Microsoft.Extensions.AI の `IEmbeddingGenerator<string, Embedding<float>>`。テンプレートは `AISettings.EmbeddingModel` (Azure OpenAI の埋め込みデプロイ名) から作る。空なら意味検索は無効 (文章だけ保存)

### ⚠ 文章にするフィールドはフロントに読み込まれていること

文章はクライアントが組み立てるので、対象フィールドの値がフロントに来ていなければ文章から欠け、その行を保存すると欠けた文章で索引が上書きされる。
詳細画面が読み込むのは「レイアウトに置いたフィールド + `DataOnlyFields` (+ それらの依存先)」だけなので、対象フィールドをレイアウトに置かないときは次のどちらかにする。

- 欠けるフィールドを詳細レイアウトの `DataOnlyFields` に入れる
- `SourceFields` を明示して、このフィールドをレイアウトか `DataOnlyFields` に置く (このフィールドは `IDataDependentField` なので、`SourceFields` が依存先として一緒に読み込まれる。ProgressField / QrCodeField と同じ仕組み)。`SourceFields` が空のときは依存先を列挙できないので、この方法は効かない

デザインチェック `SemanticSearchFieldDesign:2` が、対象フィールドの一部だけを読み込む詳細レイアウトを指摘する (対象を 1 つも読み込まないレイアウトは、そこから対象を編集できないので対象外)。

### 文章の規則 (SemanticSearchText)

- 対象フィールドを順に「表示名: 値」(値が空のフィールドは出さない)。表示名が無ければフィールド名
- 候補値 (SelectField) は表示名、リンクは表示文字列、日付は `yyyy-MM-dd`、日時は `yyyy-MM-dd HH:mm`、真偽は「はい / いいえ」、リッチテキストはタグを除いた本文、ファイルはファイル名。List / Module / Password / SemanticSearch 自身は出さない
- 改行は LF 固定。`MaxTextLength` を超えた分は切り捨て
- 更新の Submit では対象フィールドのどれかが変更されたときだけ送る (新規は常に)

### C# クラス定義 (真実の源)

外部ライブラリ `Codeer.LowCode.Blazor.Extras` で定義。`FieldDesignBase` を継承する。ランタイムの Field は UI も読み込みデータも持たず、Submit のときだけ文章 (`SemanticSearchFieldData.Text`) を送る。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / IsFocusSkip / OnFocusMoving / NextFocusField はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `SourceFields` | string[] | `[]` | 文章にする**同じモジュール内**のフィールド名。空なら DB カラムを持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成 / 更新の記録・このフィールド自身は除く)。 |
| `DbColumnText` | string | `""` | 文章を保存する DB カラム名。**書き込み専用**。 |
| `DbColumnVector` | string | `""` | 埋め込みベクトル (`[0.1,-0.2,…]` の JSON 配列テキスト) を保存する DB カラム名。**書き込み専用**。 |
| `DbColumnVectorSearch` | string | `""` | DB 側のベクトル検索 (pgvector / SQL Server 2025) で距離計算に使うベクトル型の列。空ならサーバーのメモリで比較。PostgreSQL は `DbColumnVector` をキャストする生成列の名前、SQL Server は `DbColumnVector` と同じ列名 (VECTOR 型にする)。対応しない DB (SQLite 等) では設定があっても自動でメモリ比較に落ちる。 |
| `MaxTextLength` | int | `8000` | 文章の最大文字数 (埋め込みモデルの入力上限の歯止め)。 |

`DbColumnText` / `DbColumnVector` は**両方必須** (片方だけだとデザインチェック `SemanticSearchFieldDesign:1`)。実テーブルに存在するかも検証される。`SourceFields` の各名前が同じモジュールに存在するかも検証される。

### 必要な DB 構成

いずれも文字列。ベクトルは `[0.1,-0.2,…]` の JSON 配列テキスト (1536 次元なら 15KB 前後) なので長さ制限の無い文字列型にする。

```sql
search_text   TEXT NULL,
search_vector TEXT NULL
```

DB 側のベクトル検索を使うとき (PostgreSQL + pgvector): テキスト列をキャストする生成列を作り `DbColumnVectorSearch` に設定する。SQL Server 2025 は `search_vector` 自体を `VECTOR(1536)` にして同じ名前を設定する (テキストから暗黙変換で書ける)。

```sql
-- PostgreSQL
ALTER TABLE inquiries ADD COLUMN search_vector_v vector(1536) GENERATED ALWAYS AS (search_vector::vector) STORED;
CREATE INDEX ON inquiries USING hnsw (search_vector_v vector_cosine_ops);
```

DB 側検索が使えるモジュールでは、AI チャットの `execute_sql` の SQL に `{embed:探したい内容}` と書くと質問の埋め込みリテラルに置き換わり、WHERE・JOIN・集計と距離順を 1 本の SQL で書ける (数値は AI が書かずサーバーが差し込む)。単に似た記録を挙げるだけなら `search_records`。

### JSON例

```json
{
  "SourceFields": [],
  "DbColumnText": "search_text",
  "DbColumnVector": "search_vector",
  "MaxTextLength": 8000,
  "Name": "Search",
  "IgnoreModification": false,
  "OnValidateInput": "",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign"
}
```

対象を絞る例 (件名・本文・対応内容だけを索引にする):

```json
{
  "SourceFields": ["Subject", "Body", "Response"],
  "DbColumnText": "search_text",
  "DbColumnVector": "search_vector",
  "Name": "Search",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign"
}
```

> 既定状態は [../../Defaults/SemanticSearchFieldDesign.json](../../Defaults/SemanticSearchFieldDesign.json) を参照。

### 注意

- 埋め込みモデルを変えたら全行の再索引が要る (次元が違うベクトルは検索から外れる)
- 検索はサーバーが索引を全件読んでメモリで比較する (数千〜数万行が目安)
- 文章は AI プロバイダに送られる。個人情報などを入れたくないときは `SourceFields` で絞る
- 読める範囲は `RawDataAccessOptions.DataSourceNames` で決まり、行ごとの DataReadCondition は効かない。置くページの UserReadCondition で使える人を絞る

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Text` | string | 今の行を索引用の文章にしたもの (確認用。Submit で送られるのと同じ規則。列未設定なら空)。 |

値・データ系メソッドは公開しない (`IsModified` は常に `false`)。
