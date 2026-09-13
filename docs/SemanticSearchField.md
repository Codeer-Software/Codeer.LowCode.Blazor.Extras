# SemanticSearchField - 意味検索 (AI)

モジュールの行を「内容の意味で探せる」ようにする補助フィールドです。フィールド自体に UI はなく (描画されません)、保存の裏で動きます。

- Submit のたびに、対象フィールドの値を「表示名: 値」の 1 行ずつに並べた**文章**を作って送る (クライアント側)
- サーバーがその文章の**埋め込みベクトル** (embedding) を付け、2 つの書き込み専用 DB カラムに保存する
- AI チャット ([AIChatField](AIChatField.md) の `RawDataAccessAgent`) が `search_records` ツールでこの列を使い、「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える

SQL の集計 (件数・合計) は従来どおり `execute_sql`、内容で探すのは `search_records`、と AI が使い分けます。

## 機能

- **行を文章にする規則**: 対象フィールドを順に「表示名: 値」(値が空のフィールドは出さない)。候補値 (SelectField) は表示名、リンクは表示文字列、日付は `yyyy-MM-dd`、真偽は「はい / いいえ」、リッチテキストはタグを除いた本文。改行は LF 固定 (どの環境で作っても同じ文章になる)
- **対象フィールド**: `SourceFields` で指定。空なら DB カラムを持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成 / 更新の記録・このフィールド自身は除く)
- **送るタイミング**: 新規行は常に。更新は対象フィールドのどれかが変更されたときだけ (変更が無ければ何も送らない = 埋め込みの呼び出しも起きない)
- **書き込み専用**: `DbColumnText` / `DbColumnVector` は読み戻されない (`IsWriteOnly = true`)。クライアントには値が来ない
- **埋め込みが無くても保存は止めない**: 埋め込みモデル未設定・呼び出し失敗のときは文章だけ保存し、ベクトルは NULL (警告ログ)。後から再索引で埋められる
- **再索引**: フィールドを後から置いたとき・埋め込みモデルを変えたとき・失敗した行を埋めるときに、モジュールの全行の文章とベクトルを作り直せる (`SemanticSearchIndexer.ReindexAsync`)

## 対象フィールドはフロントに読み込まれている必要がある

文章はクライアントが組み立てるので、対象フィールドの値がフロントに来ていなければ文章から欠け、その行を保存すると欠けた文章で索引が上書きされます。詳細画面が読み込むのは「レイアウトに置いたフィールド + `DataOnlyFields` (+ それらの依存先)」だけです。対象フィールドをレイアウトに置かないときは、次のどちらかにしてください。

- 欠けるフィールドを詳細レイアウトの `DataOnlyFields` に入れる
- `SourceFields` を明示して、この SemanticSearchField をレイアウトか `DataOnlyFields` に置く。SemanticSearchField は `SourceFields` を依存先として申告するので、一緒に読み込まれます (ProgressField / QrCodeField の参照先と同じ仕組み)。`SourceFields` が空のときは依存先を列挙できないので、この方法は効きません

デザインチェックが、対象フィールドの一部だけを読み込む詳細レイアウトを指摘します (`SemanticSearchFieldDesign:2`)。一覧の行を直接編集して保存する構成でも同じ条件が必要です (一覧レイアウトはチェックしないので、列か `DataOnlyFields` に対象を含めてください)。

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 (例: `Search`) |
| SourceFields | string[] | | 文章にするフィールド名 (同じモジュール)。空 = 入力フィールド全部 |
| DbColumnText | string | ○ | 文章を保存する DB カラム名 (書き込み専用) |
| DbColumnVector | string | ○ | ベクトルを保存する DB カラム名 (書き込み専用。`[0.1,-0.2,…]` の JSON 配列テキスト) |
| DbColumnVectorSearch | string | | DB 側のベクトル検索で距離計算に使うベクトル型の列 (後述)。空なら DB 側検索を使わずサーバーのメモリで比較する |
| MaxTextLength | int | | 文章の最大文字数 (既定 8000。超えた分は切り捨て。埋め込みモデルの入力上限の歯止め) |

`DbColumnText` / `DbColumnVector` は両方必要です (片方だけだとデザインチェックが指摘します)。実テーブルに存在するかもチェックされます。

## 必要な DB 構成

文章とベクトルはいずれも文字列です。ベクトルは `[0.1,-0.2,…]` の JSON 配列テキストで、1536 次元なら 15KB 前後になるので、長さ制限の無い文字列型を使ってください (この形式は pgvector や SQL Server の VECTOR 型がそのままキャストできます)。

```sql
CREATE TABLE inquiries (
    id            INTEGER PRIMARY KEY AUTOINCREMENT,
    subject       TEXT,
    body          TEXT,
    response      TEXT,
    is_deleted    INTEGER DEFAULT 0,
    search_text   TEXT,
    search_vector TEXT
);
```

## モジュール JSON 例

レイアウトに置く必要はありませんが、`Fields` には必ず含めてください。

```json
{
  "Name": "Inquiry",
  "DataSourceName": "Main",
  "DbTable": "inquiries",
  "Fields": [
    { "DbColumn": "id", "Name": "Id", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.IdFieldDesign" },
    { "DbColumn": "subject", "Name": "Subject", "DisplayName": "件名", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
    { "DbColumn": "body", "Name": "Body", "DisplayName": "本文", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
    { "DbColumn": "response", "Name": "Response", "DisplayName": "対応内容", "TypeFullName": "Codeer.LowCode.Blazor.Repository.Design.TextFieldDesign" },
    {
      "SourceFields": [],
      "DbColumnText": "search_text",
      "DbColumnVector": "search_vector",
      "MaxTextLength": 8000,
      "Name": "Search",
      "TypeFullName": "Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign"
    }
  ]
}
```

この例で保存される文章は次の形です。

```
件名: 納期の確認
本文: 先週注文したノートPC 14型がまだ届きません。いつ頃届くか教えてください。
対応内容: 物流の遅延で 2 日遅れていました。8/5 に配達完了。
```

## サーバーサイド実装が必須

フィールドを置くだけでは**ベクトルは付きません**。埋め込みモデルの用意と、保存時の索引付けはアプリ (ホスト) の責務です。Extras.Server の `Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch.SemanticSearchIndexer` を使います。

### 1. 埋め込みモデル (IEmbeddingGenerator)

[AIChatField](AIChatField.md) の `IChatClient` と同じく、どのプロバイダを使うかはアプリが決めます。Example は `AISettings` の Azure OpenAI (`EmbeddingModel` = 埋め込みのデプロイ名。`text-embedding-3-small` など) から Microsoft.Extensions.AI の `IEmbeddingGenerator<string, Embedding<float>>` を作ります (`Example/.../Extras.Server/AI/SemanticSearchIndex.cs`)。

```json
"AISettings": {
  "OpenAIEndPoint": "https://xxx.openai.azure.com/",
  "OpenAIKey": "...",
  "ChatModel": "gpt-4o",
  "EmbeddingModel": "text-embedding-3-small"
}
```

```csharp
internal static class SemanticSearchIndex
{
    public static Func<IEmbeddingGenerator<string, Embedding<float>>>? EmbeddingGeneratorFactory { get; } = Create(SystemConfig.Instance.AISettings);

    //プロセスに 1 つ
    public static SemanticSearchIndexer Indexer { get; } = new(() => EmbeddingGeneratorFactory?.Invoke());

    static Func<IEmbeddingGenerator<string, Embedding<float>>>? Create(AISettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel)) return null;
        var client = new AzureOpenAIClient(new Uri(settings.OpenAIEndPoint), new AzureKeyCredential(settings.OpenAIKey));
        var generator = client.GetEmbeddingClient(settings.EmbeddingModel).AsIEmbeddingGenerator();
        return () => generator;
    }
}
```

`EmbeddingModel` が空なら埋め込み無し = 文章だけ保存され、AI チャットに意味検索ツールは付きません。

### 2. 保存時に索引を付ける

`ModuleDataIO` の派生 (テンプレートの `CustomizedModuleDataIO`) の `AddAsync` / `UpdateAsync` で `ApplyAsync` を呼びます (PasswordHashField の `PasswordHashHelper` と同じ位置)。

```csharp
protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
{
    await SemanticSearchIndex.Indexer.ApplyAsync(_designData, data, isNewData: true);
    return await base.AddAsync(transactionId, moduleSubmitId, data);
}

protected override async Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
{
    await SemanticSearchIndex.Indexer.ApplyAsync(_designData, data, isNewData: false);
    await base.UpdateAsync(transactionId, moduleSubmitId, data);
}
```

- 文章がデータに含まれていれば (クライアントが組み立てたもの・再索引) それに埋め込みを付ける
- 新規行で文章が無ければサーバーで組み立てる (CSV 一括取込などクライアントのフィールドを通らない経路)
- 更新で文章が無いとき (対象フィールドが変わっていない) は何もしない

### 3. AI チャットに意味検索ツールを付ける

`RawDataAccessAgent` のコンストラクタの `embeddingGeneratorFactory` に同じ埋め込みモデルを渡します。デザインに (両方の列が設定された) SemanticSearchField を持つモジュールがあれば、AI に `search_records(moduleName, query, top)` ツールと「内容で探す質問はこれを使う」指示が付きます。

```csharp
new RawDataAccessAgent(chatClientFactory, () => new DbAccessor(config.DataSources), () => DesignerService.GetDesignData(), documents,
    new RawDataAccessOptions { DataSourceNames = config.AIChat.RawDataAccessDataSources },
    embeddingGeneratorFactory: SemanticSearchIndex.EmbeddingGeneratorFactory);
```

`search_records` は質問文を埋め込みにし、索引 (文章とベクトル) を読んでコサイン類似度の高い順に Id・score (0〜1)・詳細ページの URL・文章を返します。AI は行を挙げるときに詳細リンクを付け、score が低ければ「近いものは見つからなかった」と答えます。

読める範囲は `execute_sql` と同じ (`RawDataAccessOptions.DataSourceNames` のデータソースにあるモジュール)。ログインユーザーごとの行制限 (DataReadCondition) は効かないので、置くページの UserReadCondition で使える人を絞ってください ([サーバー API の権限チェック](ServerApiAuthorization.md))。論理削除された行は検索から除かれます (索引の列は残りますが、読み出し時に論理削除の列を見て飛ばします)。

### 4. 再索引 API (任意)

既存の行に索引を付けるには、モジュールの全行を読んで通常の Submit で書き直します。行は実行ユーザーの `ModuleDataIO` で読み (読み取り権限の範囲)、書き込みも通常の権限で通ります。埋め込みは 2. の `ApplyAsync` が付けます。

```csharp
[ApiController]
[Route("api/semantic_search")]
public class SemanticSearchController : ControllerBase
{
    [HttpPost("reindex/{moduleName}")]
    public async Task<ActionResult<int>> Reindex(string moduleName, CancellationToken cancellationToken)
        => await SemanticSearchIndexer.ReindexAsync(_dataService.ModuleDataIO, DesignerService.GetDesignData(), moduleName, cancellationToken: cancellationToken);
}
```

```
POST /api/semantic_search/reindex/Inquiry   → 書き直した行数
```

## DB のベクトル検索を使う (PostgreSQL / SQL Server 2025)

既定では、検索のたびにサーバーが索引を全行読んでメモリでコサイン類似度を計算します (どの DB でも動きますが、数千行が目安)。DB がベクトル型と距離関数を持つなら、距離計算を DB に任せられます。

- **PostgreSQL** (pgvector 拡張): 本体はベクトルをテキストとして書くので、`vector` 型の列には直接入りません。テキスト列をキャストする **生成列** を作り、その名前を `DbColumnVectorSearch` に設定します。

  ```sql
  CREATE EXTENSION IF NOT EXISTS vector;
  ALTER TABLE inquiries
    ADD COLUMN search_vector_v vector(1536)
    GENERATED ALWAYS AS (search_vector::vector) STORED;
  CREATE INDEX ON inquiries USING hnsw (search_vector_v vector_cosine_ops);
  ```

  ```json
  { "DbColumnVector": "search_vector", "DbColumnVectorSearch": "search_vector_v", ... }
  ```

- **SQL Server 2025**: `VECTOR` 型の列にはテキスト (JSON 配列) から暗黙変換で書けるので、`DbColumnVector` の列自体を `VECTOR(1536)` にして、`DbColumnVectorSearch` にも同じ列名を設定します (テキスト列のままにして永続化計算列 `CAST(search_vector AS VECTOR(1536))` を別に作る形でも構いません)。

`DbColumnVectorSearch` が設定されていて、接続先が対応 DB (PostgreSQL / SQL Server) のときだけ DB 側検索を使います。SQLite など対応しない DB で同じデザインを動かすと自動でメモリ比較に落ちるので、開発環境と本番でデザインを分ける必要はありません。DB 側の実行に失敗したとき (拡張が入っていない、版が古い) も警告ログを出してメモリ比較に落ちます。

DB 側検索が使えるモジュールでは、AI チャットの `execute_sql` の中でも距離計算ができます。AI は SQL に `{embed:探したい内容}` と書き、サーバーが実行前にその内容の埋め込みベクトルのリテラルに置き換えます (数値はサーバーが並べるので AI は書きません)。これで「この得意先の、今年の、未完了のクレームで似たもの」のような WHERE や JOIN・集計との組み合わせが 1 本の SQL になります。

```sql
-- AI が書く SQL の例 (PostgreSQL)
select id, subject, 1 - (search_vector_v <=> {embed:納期遅れで揉めたクレーム}) as score
from inquiries
where customer_id = 3 and status <> '9'
order by search_vector_v <=> {embed:納期遅れで揉めたクレーム}
limit 5
```

次元は埋め込みモデルで決まり列定義に固定されるので、モデルを変えたら列を作り直して再索引してください。

## Example での確認

Example の `Inquiry` (問い合わせ) モジュールが `Search` フィールドを持ちます。`SampleData/inquiry_sample.sql` を `sqlite_sample_extras.db` に適用し、`appsettings.AIChatTest.json` の `AISettings.EmbeddingModel` に埋め込みのデプロイ名を入れて、`POST api/semantic_search/reindex/Inquiry` で索引を作ってから、AI チャットに「納期遅れのクレームに似た問い合わせは?」のように聞きます。

## 注意事項

- 埋め込みモデルを変えたら (次元が変わるので) 全行の再索引が必要です。混在した行はコサイン類似度が計算できず検索結果から外れます
- 既定の検索はサーバーが索引を全件読んでメモリで比較します (ベクトル DB 不要)。数千行までが目安で、それ以上は上記の DB 側ベクトル検索 (PostgreSQL / SQL Server 2025) を使ってください
- 文章は AI (プロバイダ) に送られます。個人情報などを索引に入れたくない場合は `SourceFields` で対象を絞ってください ([AI に送られるデータ](AIChatField.md#ai-に送られるデータ))
- 文章の列は AI 向けの索引で、人が読む前提の列ではありません。画面に出すなら元のフィールドを使ってください
