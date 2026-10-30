# SemanticSearchField - 意味検索 (AI)

モジュールの行を「内容の意味で探せる」ようにする補助フィールドです。フィールド自体に UI はなく (描画されません)、保存の裏で動きます。

- 対象フィールドが変わった保存のたびに、サーバーが保存した行を読み、対象フィールドの値を「表示名: 値」の 1 行ずつに並べた**文章**を作る
- その文章の**埋め込みベクトル** (embedding) と一緒に、書き込み専用の DB カラムに保存する
- AI チャット ([AIChatField](AIChatField.md) の `RawDataAccessAgent`) が `search_records` ツールで、**DB のベクトル検索** (PostgreSQL の pgvector / SQL Server 2025 の VECTOR 型) を使って「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える

距離計算は DB が行います。ベクトル検索に対応しない DB (SQLite / MySQL / Oracle) のモジュールでは意味検索は使えません (索引の保存はどの DB でもできますが、意味検索の対象になりません。キーワード検索 (`KeywordSearch`) はどの DB でも使えます)。

SQL の集計 (件数・合計) は従来どおり `execute_sql`、内容で探すのは `search_records`、と AI が使い分けます。

同じ文章の列で**キーワード検索** (語をすべて含む行に絞る。埋め込み不要・どの DB でも動く) もできます ([キーワードで絞る](#キーワードで絞る-keywordsearch))。

## 機能

- **行を文章にする規則**: 対象フィールドを順に「表示名: 値」(値が空のフィールドは出さない)。候補値 (SelectField) は表示名、リンクは表示文字列、日付は `yyyy-MM-dd`、真偽は「はい / いいえ」、リッチテキストはタグを除いた本文。改行は LF 固定 (どの環境で作っても同じ文章になる)。`SourceFields` に書いた FileField は添付の中身も入る ([添付ファイルの中身を索引する](#添付ファイルの中身を索引する-filefield))
- **対象フィールド**: `SourceFields` で指定。空なら DB カラムを持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成 / 更新の記録・このフィールド自身は除く)。従属レコードのフィールド (TagField など。DB カラムを持たない) は空の既定には入らないので、タグ名を文章に入れるなら名前を書きます
- **作るタイミング**: 対象フィールドが変わった保存のたび (追加・対象フィールドを含む更新・タグの付け外し)。どの行を作り直すかは送信の形ではなく、本体が記録した「この保存で変更したレコード」で決めるので、編集履歴の復活・拡張の内部書き込みなど、保存の中で本体を通って書かれた行は全部対象です (一括 INSERT になる取込の行には付けません。取込の後は行数によらず `ReindexMissing()` を流してください)。保存した行をサーバーが権限を通さず読み直して作るので、保存した人がこのフィールドや対象フィールドを読めなくても索引は最新になります。クライアントは何も送りません (送っても捨てます)
- **書き込み専用**: `DbColumnText` / `DbColumnVector` は読み戻されない (`IsWriteOnly = true`)。クライアントには値が来ない
- **埋め込みが無くても保存は止めない**: 埋め込みモデル未設定・呼び出し失敗のときは文章だけ保存し、ベクトルは NULL (警告ログ)。後から再索引で埋められる
- **既存データへの一括付与 (再索引)**: 保存時の索引付けは新しく保存した行にしか効かないので、導入時に溜まっている既存データにはフィールドのスクリプト `Reindex()` で全行の文章とベクトルをまとめて付ける (1 回押せばよい。その後は保存時の索引付けで追従する)。埋め込みモデルを変えたとき・失敗した行を埋めるときも同じ操作

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Name | string | ○ | フィールド名 (例: `Search`) |
| SourceFields | string[] | | 文章にするフィールド名 (同じモジュール)。空 = DB カラムを持つ入力フィールド全部 (TagField など従属レコードのフィールドは入らない = 名前を書く) |
| DbColumnText | string | ○ | 文章を保存する DB カラム名 (書き込み専用) |
| DbColumnVector | string | ○ | ベクトルを保存する DB カラム名 (書き込み専用。`[0.1,-0.2,…]` の JSON 配列テキスト) |
| DbColumnVectorSearch | string | ○ | DB のベクトル検索で距離計算に使うベクトル型の列 (後述。PostgreSQL は `DbColumnVector` をキャストする生成列、SQL Server は `DbColumnVector` と同じ列) |
| MaxTextLength | int | | 埋め込みに渡す文章の最大文字数 (既定 8000。超えた分は埋め込みに渡さない。埋め込みモデルの入力上限の歯止め)。文章の列には全文が入るので、キーワード検索は全文に当たります |
| MaxFileTextLength | int | | `SourceFields` に書いた FileField の添付の中身を文章に入れるときの、添付 1 件あたりの最大文字数 (既定 100000。0 = 無制限) ([添付ファイルの中身を索引する](#添付ファイルの中身を索引する-filefield)) |
| SearchMaxDistance | double? | | 検索レイアウトの検索欄で探すときのコサイン距離の上限。空なら距離では絞らない ([画面の検索欄から探す](#画面の検索欄から探す-検索レイアウトに置く)) |
| KeywordSearch | bool | | 検索レイアウトの検索欄にキーワード欄も出す ([キーワードで絞る](#キーワードで絞る-keywordsearch)) |

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

ベクトル検索に対応しない DB (SQLite / MySQL / Oracle) でキーワード検索だけ使うときも列は 3 つ必要です。`DbColumnVectorSearch` は `DbColumnVector` と同じ列名 (文字列の列) でかまいません。

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
      "MaxFileTextLength": 100000,
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

フィールドを置くだけでは**ベクトルは付きません**。埋め込みモデルの用意と、保存時の索引付けはアプリ (ホスト) の責務です。Extras.Server の `Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch.SemanticSearchService` をアプリの静的な持ち物として 1 つ作り、保存時の索引付け・再索引 API・AI チャットの 3 か所から使います。

### 1. 埋め込みプロバイダ (IEmbeddingProvider)

文章をベクトルにする実装は Extras.Server の `IEmbeddingProvider` で差し替えます (メール送信の `IMailSender` と同じ作り)。Extras.Server が提供する実装と、その設定クラスは次のとおりです。設定は appsettings の独立したセクションに置き、どれを使うかは `SemanticSearch.EmbeddingProvider` の呼び名で選びます (索引と検索は同じモデルでないと成立しないので、アプリ単位の設定です)。

| 呼び名 (テンプレート) | 実装 | 設定クラス | 主な項目 |
|---|---|---|---|
| `AzureOpenAI` | `AzureOpenAIEmbeddingProvider` | `AzureOpenAIEmbeddingSettings` | EndPoint / Key / Deployment / Dimensions |
| (任意) | `EmbeddingGeneratorProvider` | なし | Microsoft.Extensions.AI の `IEmbeddingGenerator` (OllamaSharp・ONNX 等の既存実装) を包むアダプタ |

```json
"SemanticSearch": { "EmbeddingProvider": "AzureOpenAI" },
"AzureOpenAIEmbedding": {
  "EndPoint": "https://xxx.openai.azure.com/",
  "Key": "...",
  "Deployment": "text-embedding-3-small",
  "Dimensions": 1536
}
```

テンプレートの対応表 (`Extras/SemanticSearchTable.cs` の `CreateEmbeddingProvider`) が呼び名から実装を作ります。別のプロバイダ (OpenAI、Ollama などのローカルモデル、社内 API 等) は `IEmbeddingProvider` を実装して表に 1 行足すだけです (文章の配列を受けてベクトルの配列を返すメソッド 1 本)。

```csharp
//Extras/SemanticSearchTable.cs
public static IEmbeddingProvider? CreateEmbeddingProvider(string name) => name switch
{
    "AzureOpenAI" => new AzureOpenAIEmbeddingProvider(config.AzureOpenAIEmbedding),
    // "MyLocal" => new MyLocalEmbeddingProvider(...),   // 独自実装はここに足す
    _ => null,   // 呼び名が無い = 意味検索なし (文章だけ保存)
};

//同じクラスの SemanticSearchService (プロセスに 1 つ)
static readonly Lazy<IEmbeddingProvider?> _provider = new(() => CreateEmbeddingProvider(SystemConfig.Instance.SemanticSearch.EmbeddingProvider));
public static SemanticSearchService Service { get; } = new(() => _provider.Value, () => AppDesign.Store.GetDesignData());
```

`IEmbeddingProvider` は `ModelId` / `Dimensions` と `EmbedAsync(texts)` だけの小さなインターフェースです。`Dimensions` を申告しておくと、返ったベクトルの長さが違うときに保存や再索引がエラーで止まり、DB の列定義との食い違いに早く気づけます。

呼び名が空なら埋め込み無し = 文章だけ保存され、AI チャットに意味検索ツールは付きません。

### 2. 保存時に索引を付ける

`ModuleDataIO` の派生 (テンプレートの `CustomizedModuleDataIO`) のコンストラクタで `Interceptor` を登録します。保存すればその行の索引ができます。一括取込と穴埋めは `ReindexMissing()`、全部作り直すのは `Reindex()` です ([再索引](#4-既存データに一括でベクトルを付ける-再索引))。

```csharp
public CustomizedModuleDataIO(...) : base(...)
{
    // 他のインターセプタ (監査ログ・編集履歴など) より後 = 最後に登録する
    AddInterceptor(SemanticSearchTable.Service.Interceptor);
}
```

- 対象フィールドが変わった保存 (追加・対象フィールドを含む更新・タグ付けの行の付け外し) だけ、保存が終わった後にその行をサーバーが読み直して (権限を通さない内部読み) 文章と埋め込みを書き込み専用の列だけに書きます (同じトランザクション。レコードの更新ではないので、更新日時・楽観ロックの版・ExecuteSqlField は動きません)。クライアントは何も送らないので、対象フィールドがフロントに読み込まれているかどうかは関係ありません
- 追従するのは保存した行自身と、付け外しした従属レコード (タグ) の親の行までです (一覧の中の行のタグを変えても、その行の文章が作り直されます。従属レコードを別の親へ付け替えたときは元の親と新しい親の両方を作り直します)。ExecuteSqlField の SQL が他の行を書き換えた分は対象になりません (再索引で揃えます)。リンク先・候補値の表示名やタグ名を後から変えても、既存の行の文章は再索引するまで変わりません
- 埋め込みの呼び出しは保存のトランザクションの中で `SaveEmbeddingTimeout` (既定 15 秒) まで待ちます (保存時間にその往復が乗り、その間はその行のロックを持ちます)。超えたときや失敗したときは保存を止めず、文章だけ保存して警告ログを出します (後から `ReindexMissing()` で埋まります)
- 一括 INSERT で取り込んだ行 (`BulkAddThreshold` 以上の行数の取込。テンプレートは 100) には索引を付けません (文章の列もベクトルの列も空のまま。大量取込の埋め込みを保存の中で待ちません)。取込の後に `ReindexMissing()` を流すと、行から文章を作って (リンク先の表示名も入ります) 埋め込みます。それ未満の行数は 1 行ずつの保存と同じく埋め込みまで付きますが、`SaveEmbeddingTimeout` を超えると全行ベクトル無しになるので、取込の後は行数によらず `ReindexMissing()` を流すのが確実です。タグ付けのモジュール (従属レコード) そのものを一括 INSERT で取り込んだときは、親の行の文章は作り直されません。親のモジュールを `Reindex()` してください (`ReindexMissing()` はベクトルの無い行しか直しません)
- 止めないのは埋め込み (外部サービス) の失敗だけです。索引の列への書き込みが失敗したとき (埋め込みモデルを変えたのにベクトル列の次元を直していない等) は保存も失敗します (同じトランザクションの DB の失敗なので握れません。列を直してから保存し直してください)
- 旧い `ApplyAsync` は何もしません (呼び出しが残っていても索引は `Interceptor` が作ります)。送信データにこのフィールドのデータが付いていても、保存後に読み直して作り直します (索引はいつもサーバーが保存した行から作ります)
- **0.21 以前からの移行**: ホストの DbAccess を 0.2.1 以上にしてください (保存の前に同じ接続で読んでいたリクエストの保存がトランザクションの外で走っていた修正。意味検索に限らず保存全体の修正です (DbAccess のリリースノート参照)。0.2.0 のままだと「索引の列への書き込みが失敗したら保存も失敗」が成り立ちません)。`AddInterceptor(SemanticSearchIndex.Service.Interceptor)` は必須です (0.17 以前のテンプレートには無く、未登録のまま上げると保存した行の索引が作られません。旧い `ApplyAsync` の呼び出しが残っていれば、呼ばれたときにサーバーの警告ログに 1 回出ます。0.18〜0.21 の `ConditionInterceptor` は `Interceptor` に改名しました (旧名は Obsolete の別名で動きます))。`ApplyAsync` の呼び出しは全部消してください (残っていても何もしません)。一括取込の後は `ReindexMissing()` を流してください。スクリプトの `Search.Text` は無くなりました (索引はクライアントに来ません)。このフィールドを `DataOnlyFields` に置いても `SourceFields` は一緒に読み込まれなくなりました (0.21 までは依存先として申告していました。索引には関係ありませんが、その構成でスクリプトが対象フィールドの値を参照しているなら、その項目を `DataOnlyFields` に直接足してください)。再索引の API を起こせるのは「このフィールドを読める」ユーザーから「このフィールドに書ける」ユーザー (アプリアクセス条件・モジュールの UserWriteCondition・PermissionField の書き込み条件) に変わりました。0.21 で PermissionField の読み取り条件で再索引を絞っていたなら、書き込み条件に付け替えてください。警告ログ (未登録・埋め込みの失敗) は `SemanticSearchIndex.Service.LoggerFactory` (0.22 新設) を設定しないと出ません。起動時 (`Program.cs` の `var app = builder.Build();` の後) に `SemanticSearchIndex.Service.LoggerFactory = app.Services.GetRequiredService<ILoggerFactory>();` を足してください
- 保存の中で書かれた行 (インターセプタや拡張が同じ保存の中で `AddAsync` / `UpdateAsync` を呼んだ行。編集履歴の復活など) にも付きます。`Interceptor` は最後に登録してください (一番内側で動く = 索引の列への書き込みの失敗が、先に登録した監査ログや編集履歴からも保存の失敗として見えます)。内側で動くので、先に登録したインターセプタが本体の保存より後に書いた行には付きません (保存より前に書いた行には付きます)。保存 (`SubmitAsync`) の外でホストが `AddAsync` / `UpdateAsync` を直接呼ぶ経路 (`CreateSystemAccess()` の書き込み・デスクトップホストの `AddSystemRecordAsync` など) には付きません。その経路で追加した行は `ReindexMissing()`、更新した行は `Reindex()` で揃えてください
- 警告ログを出すには、アプリの起動時に `SemanticSearchTable.Service.LoggerFactory` を設定します (0.22 のテンプレートは `Program.cs` で設定済み。それより前のテンプレートから上げたホストは上の移行の項)
- 同じインターセプタが、画面の検索欄の文章を検索の前に埋め込みベクトルにします ([画面の検索欄から探す](#画面の検索欄から探す-検索レイアウトに置く))

### 3. AI チャットに意味検索ツールを付ける

`RawDataAccessAgent` のコンストラクタの `semanticSearch` に同じ `SemanticSearchService` を渡します。埋め込みプロバイダが設定されていて、デザインに (3 つの列が設定された) SemanticSearchField を持ち、データソースが PostgreSQL か SQL Server のモジュールがあれば、AI に `search_records(moduleName, query, top)` ツールと「内容で探す質問はこれを使う」という指示が付きます。

```csharp
new RawDataAccessAgent(chatClientFactory, () => new DbAccessor(config.DataSources), () => AppDesign.Store.GetDesignData(), documents,
    new RawDataAccessOptions { DataSourceNames = config.AIChat.RawDataAccessDataSources },
    semanticSearch: SemanticSearchTable.Service);
```

`search_records` は質問文を埋め込みにし、DB のベクトル検索 (PostgreSQL は pgvector の `<=>`、SQL Server は `VECTOR_DISTANCE('cosine', …)`) で似ている順に上位 N 件だけを読み、Id・score (0〜1)・詳細ページの URL・文章を返します。AI は行を挙げるときに詳細リンクを付け、score が低ければ「近いものは見つからなかった」と伝えます。DB 側の検索が失敗したとき (拡張未導入・列の型違いなど) はそのエラーが AI に返ります (サーバーで代わりに計算することはしません)。

読める範囲と使う接続は `execute_sql` と同じ (`RawDataAccessOptions.DataSourceNames`)。モジュールのデータソースがその一覧にあればその接続、無ければ一覧のうちどのモジュールも名指ししていないデータソース (AI 用に作った読み取り専用の別名接続) のうちモジュールと同じ DB 種別のもので同じ表を引きます。ログインユーザーごとの行制限 (DataReadCondition) は効かないので、置くページの UserReadCondition で使える人を絞ってください ([サーバー API の権限](AIChatField.md))。読んだ行は監査ログ ([AuditLog](AuditLog.md)) にも残りません。監査が要る環境では、`execute_sql` と同様に実行ユーザーの `ModuleDataIO` 経由で引く Agent にします (SearchCondition に意味検索の条件を足して `GetListAsync` で読む。距離計算を DB にさせる `DbColumnVectorSearch` はそのまま使えます)。

同じ表では、AI チャットの `execute_sql` の中でも距離計算ができます。AI は SQL に `{embed:探したい内容}` と書き、サーバーが実行前にその内容の埋め込みベクトルのリテラルに置き換えます (数値はサーバーが並べるので AI が書くことはありません)。WHERE・JOIN・集計と「意味の近さ」を 1 本の SQL で組み合わせられます。

```sql
-- AI が書く SQL の例 (PostgreSQL)
select id, subject, 1 - (search_vector_v <=> {embed:納期遅れで揉めたクレーム}) as score
from inquiries
where customer_id = 3 and status <> '9'
order by search_vector_v <=> {embed:納期遅れで揉めたクレーム}
limit 5
```

### 権限つきの検索 (SemanticMatchCondition)

上の `search_records` (RawDataAccessAgent) は AI 用の DB 接続で直接読むので、ログインユーザーごとの行の条件や項目の権限は効きません。
権限を効かせたいときは、意味検索を本体の一覧検索 (`ModuleDataIO.GetListAsync`) の条件として使います。`SemanticMatchCondition` (Extras) を検索条件に置くと、
SemanticSearchField が自分の列で WHERE と並びの SQL を作り、本体がいつもどおりモジュールの閲覧権限・行の条件 (DataRead)・論理削除を掛けます (本体の `FieldSqlMatchCondition` / `ISqlMatchConditionFieldDesign` の仕組み)。

```csharp
var vector = (await embeddingProvider.EmbedAsync(new[] { "納期遅れで揉めたクレーム" }))[0];   // 索引と同じ埋め込みモデル
var condition = new SearchCondition("Inquiry")
{
    Condition = MultiMatchCondition.And(
        new FieldValueMatchCondition { SearchTargetVariable = "Status.Value", Comparison = MatchComparison.NotEqual, Value = MultiTypeValue.Create("9") },
        new SemanticMatchCondition { FieldName = "Search", Vector = vector }),   // MaxDistance で距離の上限も付けられる
    LimitCount = 5,
};
condition.SortConditions.Add(new SortCondition { Variable = "Search.Value" });   // 近い順 (コサイン距離の昇順)
var rows = await moduleDataIO.GetListAsync(condition, 0);
```

- 索引の項目 (SemanticSearchField) を読めないユーザー (PermissionField) は拒否されます。この項目の読み取り条件には CurrentUser を参照する条件を使ってください (検索条件の権限判定は行を持たないので、行データを参照する条件を付けるとこの項目での検索は拒否されます。デザインチェック `PermissionFieldDesign:4` が指摘します)。距離計算は DB なので、PostgreSQL (pgvector) / SQL Server 2025 のモジュールだけです (他の DB では例外)
- `Keywords` (空白区切りの語) を入れると、文章の列がすべての語を含む行に絞ります (語ごとの LIKE の AND)。ベクトルと一緒なら絞ったうえで近い順、`Keywords` だけならどの DB でも動きます ([キーワードで絞る](#キーワードで絞る-keywordsearch))
- この条件はサーバーで実行する一覧検索だけのものです。設計に保存する条件 (権限の条件・ListField / LinkField の条件) には置けません (デザインチェックが指摘します)
- ベクトルは呼び出し側が作って入れるか、`Text` (文章) だけを入れてサーバーの前処理 (`SemanticSearchService.Interceptor`) に埋めさせます (画面の検索欄はこちら)
- AI チャットでは `ModuleDataAccessAgent` に `semanticSearch` を渡すと、この条件で検索する `search_records` が付きます ([AIChatField](AIChatField.md))

### 画面の検索欄から探す (検索レイアウトに置く)

検索レイアウトに SemanticSearchField を置くと、文章の入力欄 (「内容で探す」) が出ます。文章を入れて検索すると、一覧はその内容に近い行に絞られます (権限つきの検索と同じ仕組みで、行の条件と、このフィールド自身の読み取り権限が効きます)。

- **近い順に並べるには、一覧の並び (ListField の SearchCondition の SortConditions) に `Search.Value` を入れておきます** (例: `[Search.Value 昇順, Subject 昇順]`)。文章が空のときはこの並びは飛ばされるので、普段は次の並び (件名順) になります。列ヘッダでの並べ替えも普段どおり使えます
- 距離の上限 `SearchMaxDistance` (コサイン距離。0 = 同じ向き〜2) を設定すると、遠い行は出ません。空なら全行 (索引のある行) が対象で、並びだけが変わります
- 距離の上限が空で、一覧の並びにも `Search.Value` が無いと、文章を入れても一覧は変わりません。デザインチェックが指摘します (`SemanticSearchFieldDesign:3`。`KeywordSearch` が true のときは指摘しません = キーワードだけで使う構成のため)
- 文章を埋め込みにするのはサーバーです。ホストの `CustomizedModuleDataIO` のコンストラクタで `AddInterceptor(SemanticSearchTable.Service.Interceptor)` を登録してください (保存時の索引付けと同じインターセプタ。Cookie テンプレートは登録済み)。登録していないと「埋め込みベクトルがありません」のエラーになります
- 集計 (CrossTabField・集計チャート) を表示先にした検索でも同じ条件で絞れますが、近い順の並びは集計には関係しません
- メモリ内ページング (`IsInMemoryPaging`) の一覧では、列ヘッダで並べ替えるとメモリ上で並べ直すので近い順は失われます

### キーワードで絞る (KeywordSearch)

意味検索は「どう書いてあるか分からないが内容は分かる」ときの道具で、品番・人名・番号のように**語そのもの**で探すのには向きません (似た番号の行が混ざる・0 件になりません)。
文章の列には対象フィールドの値がそのまま入っているので、そこに部分一致を掛ければキーワード検索になります。`KeywordSearch` を `true` にすると、検索レイアウトの検索欄に**キーワード欄**が加わります。

- 空白区切りの語を**すべて含む行**に絞ります (語ごとの部分一致の AND。語の順は問わない)。`%` `_` などは文字として扱います
- 対象フィールドを横断します。リンクの表示名 (得意先名など) や候補値の表示名も文章に入っているので当たります (リンク先やタグ付けのモジュールの閲覧条件は見ません。そのモジュールを読めない人にも表示名で当たります)
- **読み取り権限**: 検索できるかはこのフィールド自身の読み取り権限で決まります (他のフィールドと同じ)。文章の列には対象フィールドの値がそのまま入るので、読ませたくない項目は `SourceFields` に入れないでください。入れるなら、このフィールドにも同じ読み取り条件 (CurrentUser で決まるもの) を付けてください。行の値で決まる条件の項目は `SourceFields` から外してください (デザインチェック: 対象フィールドに読み取り条件があるのにこのフィールドに無い構成は `SemanticSearchFieldDesign:4`、行の値で決まる条件の項目は `:5`。`:4` は条件の有無だけを見ます。同じ条件にするのは設計者の責任です。行の値で決まる条件の規則がこのフィールドも対象にしているときは代わりに本体の `PermissionFieldDesign:4` が出ます (その規則の対象からこのフィールドを外すと `:5` が出るので、その項目を `SourceFields` から外してください))
- 埋め込みは使いません。ベクトル検索に対応しない DB (SQLite / MySQL / Oracle) のモジュールや埋め込みプロバイダの無い構成でもキーワード検索は使えます (列は 3 つとも必要です)。その構成でも文章の欄 (「内容で探す」) は出ます。入れるとプロバイダ未設定または DB 非対応のエラーになります
- 文章 (意味検索) とキーワードを両方入れると、キーワードで絞ったうえで近い順になります。キーワードだけなら一覧の普段の並びです
- 大文字小文字・全角半角・かな種の扱いは文章の列の照合順序で決まります (SQL Server の既定 `Japanese_CI_AS` と MySQL の既定は区別しない、PostgreSQL / Oracle は区別する、SQLite は ASCII の大文字小文字だけ区別しない)。文章には「表示名: 値」の表示名も入っているので、項目名と同じ語は全行に当たります
- 速さは LIKE の全走査です。PostgreSQL で行数が多ければ文章の列に pg_bigm (2 文字から索引が効く) か pg_trgm の GIN インデックスを張ると、同じ検索がインデックスで走ります

```sql
-- PostgreSQL: キーワード検索を速くする (任意)
CREATE EXTENSION IF NOT EXISTS pg_bigm;
CREATE INDEX inquiries_search_text_bigm ON inquiries USING gin (search_text gin_bigm_ops);
```

AI チャット (`ModuleDataAccessAgent`) からは、`find_records` / `aggregate_records` / `cross_tab` / `search_records` の filters にこの項目の `Like` でキーワードを渡すと同じ絞り込みになります (`KeywordSearch` の設定に関係なく使えます。AI には describe_module と手順の説明で案内されます)。API からは `SemanticMatchCondition.Keywords` です。生 SQL の `RawDataAccessAgent` には「語そのもので探すときは文章の列に LIKE」と案内されます (列名つき。埋め込みプロバイダやベクトル検索に対応する DB が無くても)。

### 添付ファイルの中身を索引する (FileField)

`SourceFields` に FileField を書くと、ファイル名だけでなく**添付ファイルの中身の文字列**も文章に入ります (見積書の PDF・仕様書の Word・一覧の Excel など)。キーワード検索は全文に当たり、意味検索は今までどおり文章の先頭 `MaxTextLength` 文字を埋め込みます。

- 対象は `SourceFields` に**名前を書いた** FileField だけです。`SourceFields` が空の既定の対象には FileField は入りません (既定の構成の保存は重くなりません)
- ストレージ (ファイルシステム / Azure Blob / S3) の添付を読むには、ホストが `SemanticSearchService.FileStorages` にアプリのファイル保存先を渡します。渡していなければ今までどおりファイル名だけです (警告も出ません)。DB 列格納モード (`DbColumnFileContent`) の添付は行と一緒に読めるので、渡していなくても中身が入ります
- 文章の形: 全部のフィールドの行の後ろに、中身のある添付ごとに空行 +「表示名: ファイル名」の行 + 中身を `SourceFields` の順で足します (改行は LF・前後の空白と改行・タブ以外の制御文字は除く。中身が空なら足しません)
- 1 件あたり `MaxFileTextLength` 文字 (既定 100000。0 = 無制限) で切ります。行全体の上限は埋め込みに渡す `MaxTextLength` だけで、文章の列には全文が入ります
- 中身を取り出す方法は拡張子ごとの対応表 (`FileTextExtractors`。拡張子 → `IFileTextExtractor` の実装) で決まります。対応表に無い拡張子の添付は**読みません** (ファイル名だけ)
- 取り出しに失敗したとき (壊れたファイル・置き場所が読めない等) は警告ログ (`SemanticSearch: ...`。モジュール名・フィールド名・ファイル名) を出してその添付はファイル名だけにします。**保存は止めません**
- 保存のたび (添付を差し替えた保存・対象フィールドを書いた保存) と、行から文章を作る再索引 (`Reindex()` / 文章の無い行の `ReindexMissing()`) で読みます。保存ではトランザクションの中で添付を読むので、大きな添付ほど保存が長くなります。既存の行に中身を入れるには、結線した後に `Reindex()` を 1 回流してください

既定の対応表 (`FileTextExtractors.Default()`):

| 拡張子 | 実装 | 取り出し方 |
|---|---|---|
| `.pdf` | `PdfTextExtractor` | ページ順のテキスト (PdfPig)。画像だけの PDF (スキャン) は文字が取れません |
| `.docx` | `WordTextExtractor` | 本文の段落と表 (表は行ごとにセルをタブ区切り) |
| `.pptx` | `PowerPointTextExtractor` | スライド順のテキスト |
| `.xlsx` | `ExcelTextExtractor` | シートごとにシート名の行 + 使用範囲の行 (セルはタブ区切り。数式はファイルに保存された計算結果) |
| `.txt` `.csv` `.md` `.json` `.xml` | `PlainTextExtractor` | テキスト (BOM があればその符号化、無ければ UTF-8、読めなければ Shift_JIS) |
| `.html` `.htm` | `HtmlTextExtractor` | タグを除いた本文 |

旧形式の Office (`.doc` / `.xls`)・メール (`.eml` / `.msg`)・画像の文字認識 (OCR) は既定に入れていません (外部のサービスに送る処理を既定では行いません)。必要ならホストが `IFileTextExtractor` を実装して対応表に足します (`Extensions` に受ける拡張子 (小文字・ドット付き) を並べ、`ExtractAsync` で中身の文字列を返す。null を返すとその添付はファイル名だけ。1 つの実装が複数の拡張子を受けてもかまいません)。同じ拡張子がすでにあれば後から `Add` したものが勝ちます。既定の実装 (`FileManagement.TextExtractors` 名前空間) は単独でも使えます。

```csharp
//Extras/SemanticSearchTable.cs: アプリのファイル保存先を渡す (Example は SystemConfig.Instance.FileStorages)
public static SemanticSearchService Service { get; } = new(() => _provider.Value, () => AppDesign.Store.GetDesignData())
{
    FileStorages = () => FileStorageTable.Storages,
};

//対応表を変える (起動時に 1 回)
var extractors = FileTextExtractors.Default();
extractors.Add(new MyMailTextExtractor()); // .msg を読む実装を足す
extractors.Remove(".xlsx");               // Excel は索引に入れない
SemanticSearchTable.Service.FileTextExtractors = extractors;

//.msg を読む実装 (MyMailReader はホストが用意するもの)
public class MyMailTextExtractor : IFileTextExtractor
{
    public IEnumerable<string> Extensions => [".msg"];

    //文字列を返す。null ならその添付はファイル名だけ。content は呼び出し側が閉じる
    public async Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken)
        => await MyMailReader.ReadTextAsync(content, cancellationToken);
}
```

### 4. 既存データに一括でベクトルを付ける (再索引)

保存時の索引付け (2.) が効くのは、フィールドを置いた後に保存した行だけです。すでに溜まっている行には何も付いていないので、導入時に 1 回、**フィールドのスクリプト**の `Reindex()` で全行の文章とベクトルをまとめて作ります。以後は保存時の索引付けが追従するので、繰り返し実行する必要はありません。

導入の流れ:

1. モジュールに SemanticSearchField を置き、3 つの列を DB に作る (上記「必要な DB 構成」)
2. サーバーの結線 (1.〜3.) と埋め込みプロバイダの設定を入れてデプロイする
3. 管理用のページの ButtonField から `Reindex()` を 1 回実行する → 既存の全行にベクトルが付き、AI チャットの `search_records` で探せるようになる

同じ操作は、埋め込みモデルを変えたとき (次元が変わるので列を作り直してから全行)、`SourceFields` や対象フィールドの表示名を変えたとき (外した項目の値は既存の行の文章に残るので、外したときは必須)、一括取込の後、埋め込みに失敗して NULL のままの行を埋めるとき (`ReindexMissing()`) にも使います。ButtonField を管理用のページに置き、OnClick に書くだけです。

```csharp
// ButtonField の OnClick
void ReindexButton_OnClick()
{
    Search.Reindex();          // 全行にまとめて付ける (導入時・モデルを変えたとき)
    // Search.ReindexMissing(); // ベクトルがまだ無い行だけ (一括取込の後・失敗した行の穴埋め)
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
| `Reindex()` | 全行の文章とベクトルを行から作り直す (既存データへの一括付与・作り直し)。走っている間は無視 |
| `ReindexMissing()` | ベクトルがまだ無い行だけ。文章がある行 (保存時に埋め込みに失敗した行) はその文章をそのまま埋め込み (作り直さない)、文章も無い行 (一括取込の行) は行から作る。埋め込みモデルが無ければ、文章の無い行に文章だけ付ける |
| `CancelReindex()` | 走っている再索引を中断する |
| `IsReindexing` | 走っている間 true |
| `ReindexProcessed` / `ReindexTotal` | 書き直した行数 / 対象行数 (走っている間は途中経過) |
| `ReindexError` | 最後の再索引のエラー (成功なら空。中断も文言が入る) |
| `SearchText` / `KeywordText` | 検索レイアウトの文章の欄 / キーワード欄の値 (スクリプトで設定すれば、欄を出していなくても条件に入る) |

- スクリプトから `Search` を参照するには、SemanticSearchField がそのレイアウトの `DataOnlyFields` (またはレイアウト) に入っている必要があります (UI が無いので DataOnlyFields が自然です)
- 誰が起こせるかは権限で決まります。再索引は索引を書き直す操作なので、API はその SemanticSearchField に今のユーザーがユーザー権限だけで書けるとき (アプリアクセス条件・モジュールの UserWriteCondition・PermissionField の書き込み条件) だけ受け付けます。どれも無い既定の構成では、アプリに入れる人全員が起こせます (全行の埋め込み = 従量課金)。管理者などに絞るには、このフィールドを対象にした PermissionField の書き込み条件 (CurrentUser で決まるもの) を付けてください。ボタンを置くページの UserReadCondition は画面を隠すだけで、API は絞りません
- 索引は保存時と同じく、サーバーが権限を通さず全行を読んで書き込み専用の列だけに書きます (誰が起こしても同じ文章になります。レコードの更新ではないので、更新日時・楽観ロックの版は変わらず、監査ログ・編集履歴にも残りません)
- サーバーではジョブとして走り、ページ (既定 100 行) ごとに文章をまとめて 1 回で埋め込みます。同じモジュールの再索引が走っている間に起こすと、その進捗に合流します (欠損だけの再索引が走っている間に全行を起こすのは合流できないのでエラー)
- 運用中に走らせると、再索引が行を読んでから書くまでの間に保存した行の索引が古い文章で上書きされることがあります (次の保存か再索引で直ります)
- 埋め込みモデルが無いときは文章だけ書き直します。埋め込みの失敗はエラーとして返ります (保存時と違い黙って NULL にはしません)

サーバー側は共通の API (`Controllers/ExtrasController.cs` = `POST api/extras`) の対応表 `Extras/ExtrasApiTable.cs` に `SemanticSearchApi` を並べます。`SemanticSearchApi` (Extras.Server) が再索引の開始・状態・中断を受け、同じ `SemanticSearchService` の `StartReindexAsync` / `GetReindexStatus` / `CancelReindex` を呼びます。ページの行数・ジョブの保持時間・保存時の埋め込みの上限時間は `ReindexPageSize` / `FinishedRetention` / `MaxRunning` / `SaveEmbeddingTimeout` プロパティ。バックグラウンドではリクエストの HttpContext が無いので、ユーザー Id を固定した DataService を開いて渡します。

```csharp
//Extras/ExtrasApiTable.cs
new SemanticSearchApi(SemanticSearchTable.Service, userId =>
{
    var dataService = new DataService(userId);
    return new SemanticSearchReindexScope(dataService.ModuleDataIO, dataService.DbAccess, dataService);
}),
```

クライアント側の設定は不要です (共通の API `ExtrasTransport.EndPoint` = `/api/extras` に送ります)。再索引専用のコントローラで受ける場合は、従来どおり `SemanticSearchField.EndPoint` にその URL (POST {EndPoint} / GET・DELETE {EndPoint}/{requestId}) を設定すればそちらに送ります。

Example (`Source/Example/Extras`) には索引付けの結線 (`Services/CustomizedModuleDataIO.cs`)・埋め込みプロバイダの対応表と `SemanticSearchService` の持ち物 (`AI/EmbeddingProviderTable.cs` / `AI/SemanticSearchIndex.cs`)・エージェントへの結線 (`AI/AIChatAgentTable.cs`) が入っています。Example のデータは SQLite なので意味検索できるモジュールは置いていません。動かすには PostgreSQL (pgvector) のデータソースに上記の構成でモジュールを作ってください。

## 注意事項

- 意味検索できるのは PostgreSQL (pgvector 拡張) と SQL Server 2025 のデータソースだけです。SQLite / MySQL / Oracle のモジュールに置いても索引は保存されますが、AI チャットの意味検索 (search_records) の対象にはなりません (キーワードの絞り込みは使えます)
- 0.20 以前からの移行: 文章の列には全文が入るようになりました (0.20 までは `MaxTextLength` で切っていました。今は埋め込みに渡す分だけ切ります)。列を長さ制限つきの型で作っていたら制限の無い型 (TEXT / NVARCHAR(MAX)) に広げてください (超えると保存が失敗します)。既存の行の文章は切られたままなので、全文にキーワードを当てたいなら `Reindex()` を一度流してください
- 埋め込みモデルを変えたら (次元が変わるので) ベクトル列を作り直して全行の再索引が必要です
- 文章は AI (プロバイダ) に送られます。個人情報などを索引に入れたくない場合は `SourceFields` で対象を絞ってください ([AI に送られるデータ](AIChatField.md#ai-に送られるデータ))
- 文章の列は AI 向けの索引で、人が読む前提の列ではありません。画面に出すなら元のフィールドを使ってください
- リンクの表示名は保存時点の値で文章に入ります。リンク先 (得意先名など) を改名しても、既存の行は再索引するまで旧名のままです
