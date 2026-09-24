# SemanticSearchField - 意味検索 (AI)

モジュールの行を「内容の意味で探せる」ようにする補助フィールドです。フィールド自体に UI はなく (描画されません)、保存の裏で動きます。

- Submit のたびに、対象フィールドの値を「表示名: 値」の 1 行ずつに並べた**文章**を作って送る (クライアント側)
- サーバーがその文章の**埋め込みベクトル** (embedding) を付け、書き込み専用の DB カラムに保存する
- AI チャット ([AIChatField](AIChatField.md) の `RawDataAccessAgent`) が `search_records` ツールで、**DB のベクトル検索** (PostgreSQL の pgvector / SQL Server 2025 の VECTOR 型) を使って「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える

距離計算は DB が行います。ベクトル検索に対応しない DB (SQLite / MySQL / Oracle) のモジュールでは意味検索は使えません (索引の保存はどの DB でもできますが、検索の対象になりません)。

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
| DbColumnVectorSearch | string | ○ | DB のベクトル検索で距離計算に使うベクトル型の列 (後述。PostgreSQL は `DbColumnVector` をキャストする生成列、SQL Server は `DbColumnVector` と同じ列) |
| MaxTextLength | int | | 文章の最大文字数 (既定 8000。超えた分は切り捨て。埋め込みモデルの入力上限の歯止め) |

3 つのカラムはすべて必要です (欠けるとデザインチェック `SemanticSearchFieldDesign:1`)。実テーブルに存在するかもチェックされます。

## 必要な DB 構成

文章とベクトルはいずれも文字列として書き込まれます。ベクトルは `[0.1,-0.2,…]` の JSON 配列テキストで、1536 次元なら 15KB 前後になるので、長さ制限の無い文字列型を使ってください。この形式は pgvector や SQL Server の VECTOR 型がそのままキャストできます。次元は埋め込みモデルで決まり列定義に固定されるので、モデルを変えたら列を作り直して再索引してください。

- **PostgreSQL** (pgvector 拡張): 本体はベクトルをテキストとして書くので、`vector` 型の列には直接入りません。テキスト列をキャストする**生成列**を作り、その名前を `DbColumnVectorSearch` に設定します。

  ```sql
  CREATE EXTENSION IF NOT EXISTS vector;
  CREATE TABLE inquiries (
      id              SERIAL PRIMARY KEY,
      subject         TEXT,
      body            TEXT,
      response        TEXT,
      is_deleted      BOOLEAN DEFAULT FALSE,
      search_text     TEXT,
      search_vector   TEXT,
      search_vector_v vector(1536) GENERATED ALWAYS AS (search_vector::vector) STORED
  );
  CREATE INDEX ON inquiries USING hnsw (search_vector_v vector_cosine_ops);
  ```

  ```json
  { "DbColumnText": "search_text", "DbColumnVector": "search_vector", "DbColumnVectorSearch": "search_vector_v" }
  ```

- **SQL Server 2025**: `VECTOR` 型の列にはテキスト (JSON 配列) から暗黙変換で書けるので、`DbColumnVector` の列自体を `VECTOR(1536)` にして、`DbColumnVectorSearch` にも同じ列名を設定します。

  ```sql
  search_text   NVARCHAR(MAX) NULL,
  search_vector VECTOR(1536) NULL
  ```

  ```json
  { "DbColumnText": "search_text", "DbColumnVector": "search_vector", "DbColumnVectorSearch": "search_vector" }
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
      "DbColumnVectorSearch": "search_vector_v",
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

### 1. 埋め込みプロバイダ (IEmbeddingProvider)

文章をベクトルにする実装は Extras.Server の `IEmbeddingProvider` で差し替えます (メール送信の `IMailSender` と同じ作り)。Extras.Server が提供する実装と、その設定クラスは次のとおりです。設定は appsettings の独立したセクションに置き、どれを使うかは `SemanticSearch.EmbeddingProvider` の呼び名で選びます (索引と検索は同じモデルでないと成立しないので、アプリ単位の設定です)。

| 呼び名 (テンプレート) | 実装 | 設定クラス | 主な項目 |
|---|---|---|---|
| `AzureOpenAI` | `AzureOpenAIEmbeddingProvider` | `AzureOpenAIEmbeddingSettings` | EndPoint / Key / Deployment / Dimensions |
| `OpenAI` | `OpenAIEmbeddingProvider` | `OpenAIEmbeddingSettings` | Key / Model / Dimensions |
| `Ollama` | `OllamaEmbeddingProvider` | `OllamaEmbeddingSettings` | BaseUrl / Model / Dimensions / BatchSize。ローカル / 社内サーバーのモデル。文章が外に出ない |
| (任意) | `EmbeddingGeneratorProvider` | なし | Microsoft.Extensions.AI の `IEmbeddingGenerator` (OllamaSharp・ONNX 等) を包むアダプタ |

```json
"SemanticSearch": { "EmbeddingProvider": "AzureOpenAI" },
"AzureOpenAIEmbedding": {
  "EndPoint": "https://xxx.openai.azure.com/",
  "Key": "...",
  "Deployment": "text-embedding-3-small",
  "Dimensions": 1536
},
"OllamaEmbedding": { "BaseUrl": "http://localhost:11434", "Model": "bge-m3", "Dimensions": 1024 }
```

テンプレートの対応表 (`AI/EmbeddingProviderTable.cs`) が呼び名から実装を作ります。独自の埋め込み (ONNX のローカルモデル、社内 API 等) は `IEmbeddingProvider` を実装して表に 1 行足すだけです。

```csharp
public static IEmbeddingProvider? Create(string name) => name switch
{
    "AzureOpenAI" => new AzureOpenAIEmbeddingProvider(config.AzureOpenAIEmbedding),
    "OpenAI" => new OpenAIEmbeddingProvider(config.OpenAIEmbedding),
    "Ollama" => new OllamaEmbeddingProvider(config.OllamaEmbedding),
    _ => null,   // 呼び名が無い = 意味検索なし (文章だけ保存)
};

//AI/SemanticSearchIndex.cs (プロセスに 1 つ)
public static IEmbeddingProvider? Provider { get; } = EmbeddingProviderTable.Create(SystemConfig.Instance.SemanticSearch.EmbeddingProvider);
public static SemanticSearchIndexer Indexer { get; } = new(() => Provider);
```

`IEmbeddingProvider` は `ModelId` / `Dimensions` と `EmbedAsync(texts)` だけの小さなインターフェースです。`Dimensions` を申告しておくと、返ったベクトルの長さが違うときに保存や再索引がエラーで止まり、DB の列定義との食い違いに早く気づけます。

呼び名が空なら埋め込み無し = 文章だけ保存され、AI チャットに意味検索ツールは付きません。

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

`RawDataAccessAgent` のコンストラクタの `embeddingProvider` に同じ埋め込みプロバイダを渡します。デザインに (3 つの列が設定された) SemanticSearchField を持ち、データソースが PostgreSQL か SQL Server のモジュールがあれば、AI に `search_records(moduleName, query, top)` ツールと「内容で探す質問はこれを使う」という指示が付きます。

```csharp
new RawDataAccessAgent(chatClientFactory, () => new DbAccessor(config.DataSources), () => DesignerService.GetDesignData(), documents,
    new RawDataAccessOptions { DataSourceNames = config.AIChat.RawDataAccessDataSources },
    embeddingProvider: () => SemanticSearchIndex.Provider!);
```

`search_records` は質問文を埋め込みにし、DB のベクトル検索 (PostgreSQL は pgvector の `<=>`、SQL Server は `VECTOR_DISTANCE('cosine', …)`) で似ている順に上位 N 件だけを読み、Id・score (0〜1)・詳細ページの URL・文章を返します。AI は行を挙げるときに詳細リンクを付け、score が低ければ「近いものは見つからなかった」と伝えます。DB 側の検索が失敗したとき (拡張未導入・列の型違いなど) はそのエラーが AI に返ります (サーバーで代わりに計算することはしません)。

読める範囲は `execute_sql` と同じ (`RawDataAccessOptions.DataSourceNames` のデータソースにあるモジュール)。ログインユーザーごとの行制限 (DataReadCondition) は効かないので、置くページの UserReadCondition で使える人を絞ってください ([サーバー API の権限](AIChatField.md))。

同じ表では、AI チャットの `execute_sql` の中でも距離計算ができます。AI は SQL に `{embed:探したい内容}` と書き、サーバーが実行前にその内容の埋め込みベクトルのリテラルに置き換えます (数値はサーバーが並べるので AI が書くことはありません)。WHERE・JOIN・集計と「意味の近さ」を 1 本の SQL で組み合わせられます。

```sql
-- AI が書く SQL の例 (PostgreSQL)
select id, subject, 1 - (search_vector_v <=> {embed:納期遅れで揉めたクレーム}) as score
from inquiries
where customer_id = 3 and status <> '9'
order by search_vector_v <=> {embed:納期遅れで揉めたクレーム}
limit 5
```

### 4. 再索引 (溜まっている行全部の文章とベクトルを作り直す)

フィールドを後から置いたとき・埋め込みモデルを変えたとき・埋め込みに失敗した行を埋めるときは、**フィールドのスクリプト**から再索引を起こします。ButtonField を管理用のページに置き、OnClick に書くだけです。

```csharp
// ButtonField の OnClick
void ReindexButton_OnClick()
{
    Search.Reindex();          // 全行を作り直す (モデルを変えたとき等)
    // Search.ReindexMissing(); // ベクトルがまだ無い行だけ (失敗した行の穴埋め)
}

// SemanticSearchField の OnReindexCompleted (終わったとき。成功・失敗・中断のどれでも呼ばれる)
void Search_OnReindexCompleted()
{
    if (Search.ReindexError != "") MessageBox.Show(Search.ReindexError);
    else MessageBox.Show($"{Search.ReindexProcessed} 件を索引しました");
}
```

| メンバー | 説明 |
|---|---|
| `Reindex()` | 読める全行の文章とベクトルを作り直す。走っている間は無視 |
| `ReindexMissing()` | ベクトルがまだ無い行だけ |
| `CancelReindex()` | 走っている再索引を中断する |
| `IsReindexing` | 走っている間 true |
| `ReindexProcessed` / `ReindexTotal` | 書き直した行数 / 対象行数 (走っている間は途中経過) |
| `ReindexError` | 最後の再索引のエラー (成功なら空。中断も文言が入る) |

- スクリプトから `Search` を参照するには、SemanticSearchField がそのレイアウトの `DataOnlyFields` (またはレイアウト) に入っている必要があります (UI が無いので DataOnlyFields が自然です)
- 誰が起こせるかは権限で決まります。API はその SemanticSearchField を今のユーザーがユーザー権限だけで読めるとき (アプリアクセス条件・モジュールの UserReadCondition・PermissionField) だけ受け付け、行の読み書きは実行ユーザーの ModuleDataIO で行います (読める行だけ・書ける行だけ)。ボタンを置くページの UserReadCondition でも絞れます
- サーバーではジョブとして走り、ページ (既定 100 行) ごとに文章をまとめて 1 回で埋め込みます。同じモジュールの再索引が走っている間に起こすと、その進捗に合流します
- 埋め込みモデルが無いときは文章だけ書き直します。埋め込みの失敗はエラーとして返ります (保存時と違い黙って NULL にはしません)

サーバー側は Extras.Server の `SemanticSearchReindexJobStore` をアプリの静的な持ち物として 1 つ作り、AIChat と同じ形の薄い Controller から使います (Example の `Controllers/SemanticSearchController.cs` / `AI/SemanticSearchIndex.cs`)。バックグラウンドではリクエストの HttpContext が無いので、ユーザー Id を固定した DataService を開いて渡します。

```csharp
//AI/SemanticSearchIndex.cs
public static SemanticSearchReindexJobStore Jobs { get; } = new(Indexer, () => DesignerService.GetDesignData());

//Controllers/SemanticSearchController.cs ([Route("api/semantic_search/reindex")])
[HttpPost]
public async Task<ActionResult<SemanticSearchReindexResponse>> Start([FromBody] SemanticSearchReindexRequest request)
{
    var userId = await _dataService.GetCurrentUserIdAsync();
    var requestId = await SemanticSearchIndex.Jobs.StartAsync(Owner, request, _dataService.ModuleDataIO, () =>
    {
        var dataService = new DataService(userId);
        return new SemanticSearchReindexScope(dataService.ModuleDataIO, dataService.DbAccess, dataService);
    });
    return Accepted(new SemanticSearchReindexResponse { RequestId = requestId });
}

[HttpGet("{requestId}")]    // → SemanticSearchReindexStatusResponse (Status / Processed / Total / Error)
[HttpDelete("{requestId}")] // 中断
```

```csharp
// クライアント (ServiceInitializer)。URL はアプリの持ち物なので起動時に一度設定する
SemanticSearchField.EndPoint = "/api/semantic_search/reindex";
```

Example (`Source/Example/Extras`) には索引付け (`Services/CustomizedModuleDataIO.cs`)・埋め込みプロバイダの対応表とジョブ置き場 (`AI/EmbeddingProviderTable.cs` / `AI/SemanticSearchIndex.cs`)・再索引 API (`Controllers/SemanticSearchController.cs`)・エージェントへの結線 (`AI/AIChatAgentTable.cs`) が入っています。Example のデータは SQLite なので意味検索できるモジュールは置いていません。動かすには PostgreSQL (pgvector) のデータソースに上記の構成でモジュールを作ってください。

## 注意事項

- 意味検索できるのは PostgreSQL (pgvector 拡張) と SQL Server 2025 のデータソースだけです。SQLite / MySQL / Oracle のモジュールに置いても索引は保存されますが、AI チャットの検索対象にはなりません
- 埋め込みモデルを変えたら (次元が変わるので) ベクトル列を作り直して全行の再索引が必要です
- 文章は AI (プロバイダ) に送られます。個人情報などを索引に入れたくない場合は `SourceFields` で対象を絞ってください ([AI に送られるデータ](AIChatField.md#ai-に送られるデータ))
- 文章の列は AI 向けの索引で、人が読む前提の列ではありません。画面に出すなら元のフィールドを使ってください
