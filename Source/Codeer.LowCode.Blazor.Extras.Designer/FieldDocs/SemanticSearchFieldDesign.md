## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

モジュールの行を **内容の意味で探せる** ようにする書き込み専用フィールド (UI なし)。Submit のたびに `SourceFields` の値を「表示名: 値」の 1 行ずつに並べた文章を作って送り、サーバーがその文章の埋め込みベクトル (embedding) を付けて、書き込み専用の列に保存する。

AI チャット ([AIChatField](AIChatFieldDesign.md) の `RawDataAccessAgent`) はこの索引を `search_records` ツールで使い、「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える (件数・合計などの集計は SQL のまま)。**距離計算は DB のベクトル検索 (PostgreSQL の pgvector / SQL Server 2025 の VECTOR 型) が行う**。それ以外の DB (SQLite / MySQL / Oracle) のモジュールに置いても索引は保存されるが、検索の対象にはならない。

> [PasswordHashField](PasswordHashFieldDesign.md) と同型の「入力欄を持たず、保存の裏で列を書く」フィールド。レイアウトに置く必要はないが `Fields` には必ず含める。

### ⚠ サーバ側の実装が必須（重要）

このフィールドを置くだけでは**ベクトルは付かない**。埋め込みモデルの用意と保存時の索引付けはホストアプリの責務で、Extras.Server の `Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch.SemanticSearchIndexer` を使う。

- 保存時: `ModuleDataIO` の派生 (通常 `CustomizedModuleDataIO.AddAsync` / `UpdateAsync`) で `await indexer.ApplyAsync(designData, data, isNewData)` を呼ぶ。送られてきた文章に埋め込みを付ける (新規で文章が無ければサーバーで組み立てる = 一括取込)。埋め込みモデル未設定・失敗のときは文章だけ保存 (ベクトル NULL・警告ログ)
- AI チャット: `RawDataAccessAgent` のコンストラクタ `embeddingProvider` に埋め込みプロバイダを渡す。渡したときだけ `search_records` が付く (対象は 3 列が設定済みで、データソースが PostgreSQL / SQL Server のモジュール)
- 再索引: ホストは `SemanticSearchReindexJobStore` を静的に 1 つ持ち、AIChat と同じ形の Controller (POST / GET / DELETE `api/semantic_search/reindex`) から使う。クライアントは `SemanticSearchField.EndPoint` にその URL を設定する。デザイン側はフィールドのスクリプト `Reindex()` を ButtonField から呼ぶだけ
- 埋め込みは Extras.Server の `IEmbeddingProvider` (メールの IMailSender と同じ作り)。実装は `AzureOpenAIEmbeddingProvider` / `OpenAIEmbeddingProvider` / `OllamaEmbeddingProvider` (ローカルモデル) と、Microsoft.Extensions.AI の生成器を包む `EmbeddingGeneratorProvider`。テンプレートは appsettings の `SemanticSearch.EmbeddingProvider` の呼び名で対応表 (`EmbeddingProviderTable`) から選ぶ。空なら意味検索は無効 (文章だけ保存)。モデルを変えたら列の次元を合わせて作り直し、`Reindex()` で全行再索引

### ⚠ 文章にするフィールドはフロントに読み込まれていること

文章はクライアントが組み立てるので、対象フィールドの値がフロントに来ていなければ文章から欠け、その行を保存すると欠けた文章で索引が上書きされる。
詳細画面が読み込むのは「レイアウトに置いたフィールド + `DataOnlyFields` (+ それらの依存先)」だけなので、対象フィールドをレイアウトに置かないときは次のどちらかにする。

- 欠けるフィールドを詳細レイアウトの `DataOnlyFields` に入れる
- `SourceFields` を明示して、このフィールドをレイアウトか `DataOnlyFields` に置く (このフィールドは `IDataDependentField` なので、`SourceFields` が依存先として一緒に読み込まれる。ProgressField / QrCodeField と同じ仕組み)。`SourceFields` が空のときは依存先を列挙できないので効かない

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
| `DbColumnText` | string | `""` | 文章を保存する DB カラム名。**書き込み専用・必須**。 |
| `DbColumnVector` | string | `""` | 埋め込みベクトル (`[0.1,-0.2,…]` の JSON 配列テキスト) を保存する DB カラム名。**書き込み専用・必須**。 |
| `DbColumnVectorSearch` | string | `""` | DB のベクトル検索で距離計算に使うベクトル型の列。**必須**。PostgreSQL は `DbColumnVector` をキャストする生成列の名前、SQL Server は `DbColumnVector` と同じ列名 (VECTOR 型にする)。 |
| `MaxTextLength` | int | `8000` | 文章の最大文字数 (埋め込みモデルの入力上限の歯止め)。 |
| `OnReindexCompleted` | string | `""` | スクリプトの `Reindex()` / `ReindexMissing()` で起こした再索引が終わった (成功・失敗・中断) ときに呼ぶスクリプト関数名。結果は `ReindexProcessed` / `ReindexError` で見る。 |

3 つのカラムは**すべて必須** (欠けるとデザインチェック `SemanticSearchFieldDesign:1`)。実テーブルに存在するかも検証される。`SourceFields` の各名前が同じモジュールに存在するかも検証される。

### 必要な DB 構成

文章とベクトルは文字列として書き込まれる。ベクトルは `[0.1,-0.2,…]` の JSON 配列テキスト (1536 次元なら 15KB 前後)。次元は埋め込みモデルで決まり列定義に固定される (モデルを変えたら列を作り直して再索引)。

```sql
-- PostgreSQL (pgvector): テキスト列をキャストする生成列を DbColumnVectorSearch にする
CREATE EXTENSION IF NOT EXISTS vector;
search_text     TEXT NULL,
search_vector   TEXT NULL,
search_vector_v vector(1536) GENERATED ALWAYS AS (search_vector::vector) STORED
CREATE INDEX ON inquiries USING hnsw (search_vector_v vector_cosine_ops);

-- SQL Server 2025: search_vector 自体を VECTOR 型にして DbColumnVectorSearch にも同じ名前 (テキストから暗黙変換で書ける)
search_text   NVARCHAR(MAX) NULL,
search_vector VECTOR(1536) NULL
```

AI チャットの `execute_sql` の SQL に `{embed:探したい内容}` と書くと質問の埋め込みリテラルに置き換わり、WHERE・JOIN・集計と距離順を 1 本の SQL で書ける (数値は AI が書かずサーバーが差し込む)。単に似た記録を挙げるだけなら `search_records`。

### JSON例

```json
{
  "SourceFields": [],
  "DbColumnText": "search_text",
  "DbColumnVector": "search_vector",
  "DbColumnVectorSearch": "search_vector_v",
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
  "DbColumnVectorSearch": "search_vector_v",
  "Name": "Search",
  "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign"
}
```

> 既定状態は [../../Defaults/SemanticSearchFieldDesign.json](../../Defaults/SemanticSearchFieldDesign.json) を参照。

### 注意

- 意味検索できるのは PostgreSQL (pgvector) と SQL Server 2025 のデータソースだけ。SQLite / MySQL / Oracle のモジュールは検索対象にならない
- 埋め込みモデルを変えたらベクトル列を作り直して全行の再索引が要る (次元が違うベクトルは入らない)
- 文章は AI プロバイダに送られる。個人情報などを入れたくないときは `SourceFields` で絞る
- 読める範囲は `RawDataAccessOptions.DataSourceNames` で決まり、行ごとの DataReadCondition は効かない。置くページの UserReadCondition で使える人を絞る

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Text` | string | 今の行を索引用の文章にしたもの (確認用。Submit で送られるのと同じ規則。列未設定なら空)。 |
| `Reindex()` | void | 読める全行の文章とベクトルを作り直す (埋め込みモデルを変えたとき・フィールドを後から置いたとき)。サーバーのジョブとして走る。走っている間は無視。 |
| `ReindexMissing()` | void | ベクトルがまだ無い行だけ索引を付ける (埋め込みに失敗した行の穴埋め)。 |
| `CancelReindex()` | void | 走っている再索引を中断する。 |
| `IsReindexing` | bool | 再索引が走っている間 true。 |
| `ReindexProcessed` | int | 書き直した行数 (走っている間は途中経過)。 |
| `ReindexTotal` | int | 対象の行数 (分かるまでは 0)。 |
| `ReindexError` | string | 最後の再索引のエラー (成功なら空。中断も文言が入る)。 |

再索引を ButtonField から起こす例 (このフィールドはボタンを置くレイアウトの `DataOnlyFields` に入れる):

```csharp
void ReindexButton_OnClick()
{
    Search.Reindex();
}

void Search_OnReindexCompleted()
{
    if (Search.ReindexError != "") MessageBox.Show(Search.ReindexError);
    else MessageBox.Show($"{Search.ReindexProcessed} 件を索引しました");
}
```

誰が起こせるかは権限で決まる: API はこのフィールドを今のユーザーがユーザー権限だけで読めるとき (アプリアクセス条件・モジュールの UserReadCondition・PermissionField) だけ受け付け、行の読み書きは実行ユーザーの権限で通る。ボタンを置くページの UserReadCondition でも絞れる。

値・データ系メソッドは公開しない (`IsModified` は対象フィールドのどれかが変更されているときだけ `true`)。
