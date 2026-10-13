## Design

**TypeFullName:** `Codeer.LowCode.Blazor.Extras.Designs.SemanticSearchFieldDesign`

**外部ライブラリ:** `Codeer.LowCode.Blazor.Extras`

モジュールの行を **内容の意味で探せる** ようにする書き込み専用フィールド (UI なし)。対象フィールドが変わった保存のたびにサーバーが保存した行を権限を通さず読み、`SourceFields` の値を「表示名: 値」の 1 行ずつに並べた文章とその埋め込みベクトル (embedding) を、書き込み専用の列に保存する。クライアントは何も送らない。

AI チャット ([AIChatField](AIChatFieldDesign.md) の `RawDataAccessAgent`) はこの索引を `search_records` ツールで使い、「似た事例」「〜のような問い合わせ」のように内容で探す質問に答える (件数・合計などの集計は SQL のまま)。**距離計算は DB のベクトル検索 (PostgreSQL の pgvector / SQL Server 2025 の VECTOR 型) が行う**。それ以外の DB (SQLite / MySQL / Oracle) のモジュールに置いても索引は保存されるが、検索の対象にはならない。

> [PasswordHashField](PasswordHashFieldDesign.md) と同型の「入力欄を持たず、保存の裏で列を書く」フィールド。レイアウトに置く必要はないが `Fields` には必ず含める。

### ⚠ サーバ側の実装が必須（重要）

このフィールドを置くだけでは**ベクトルは付かない**。埋め込みモデルの用意と保存時の索引付けはホストアプリの責務で、Extras.Server の `Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch.SemanticSearchService` を 1 つ作って使う (索引付け・再索引 API・AI チャットの意味検索が同じ入口)。

- 保存時: `ModuleDataIO` の派生 (通常 `CustomizedModuleDataIO`) のコンストラクタで `AddInterceptor(semanticSearch.Interceptor)`。保存が終わった後にサーバーが行を読み直して (権限を通さない内部読み) 文章と埋め込みを書き込み専用の列だけに書く (レコードの更新ではない = 更新日時・楽観ロックの版・ExecuteSqlField は動かない)。対象フィールドが変わった保存 (追加・対象フィールドを含む更新・タグの付け外し) だけ作り直す。どの行かは送信の形ではなく本体が記録した「この保存で変更したレコード」で決める (取込・編集履歴の復活・拡張の内部書き込みも対象。クライアントが送った索引データは捨てる)。一括 INSERT (`BulkAddAsync`) だけは `await semanticSearch.ApplyAsync(datas)` で取り込む行のデータから文章だけ作る (ベクトルとリンク表示名は取込後の `ReindexMissing()`)。埋め込みの失敗は文章だけ保存して止めないが、索引の列への書き込みの失敗は保存も失敗 (同じトランザクション)。警告ログは起動時に `SemanticSearchIndex.Service.LoggerFactory` を設定して出す (テンプレートは設定済み)。リンク先の表示名・タグ名を後から変えても既存の行は再索引まで変わらない。埋め込みの往復は保存のトランザクションの中で `SaveEmbeddingTimeout` (既定 15 秒) まで待つ (超過・失敗は文章だけ保存 + 警告ログ。後から `ReindexMissing()`)。保存 (`SubmitAsync`) の外でホストが `AddAsync` / `UpdateAsync` を直接呼ぶ経路には付かない (内部の追加は `ApplyAsync` で文章だけ、更新は再索引。`ApplyAsync` は渡した項目だけで文章を作るので更新データには使えない)。埋め込みモデル未設定・失敗のときは文章だけ保存 (ベクトル NULL・警告ログ)
- AI チャット: `RawDataAccessAgent` / `ModuleDataAccessAgent` のコンストラクタ `semanticSearch` に同じ `SemanticSearchService` を渡す。渡していて埋め込みプロバイダが設定されているときだけ `search_records` が付く (対象は 3 列が設定済みで、データソースが PostgreSQL / SQL Server のモジュール)。`ModuleDataAccessAgent` の `search_records` は本体の一覧検索の条件 (`SemanticMatchCondition`) として走るので、行の条件・項目の読み取り権限が効く (RawDataAccess のほうは AI 用 DB ユーザーの範囲)
- 再索引 (既存データへの一括付与): 保存時の索引付けは新しく保存した行にしか効かないので、導入時に溜まっている既存データにはフィールドのスクリプト `Reindex()` で全行にまとめてベクトルを付ける (1 回。以後は保存時に追従)。`SourceFields` を変えたとき (外した項目の値は既存の行に残る)・一括取込の後・埋め込みモデルを変えたときも再索引。ホストは同じ `SemanticSearchService` の `StartReindexAsync` / `GetReindexStatus` / `CancelReindex` を、AIChat と同じ形の Controller (POST / GET / DELETE `api/semantic_search/reindex`) から使う。クライアントは `SemanticSearchField.EndPoint` にその URL を設定する。デザイン側はフィールドのスクリプト `Reindex()` を ButtonField から呼ぶだけ
- 埋め込みは Extras.Server の `IEmbeddingProvider` (メールの IMailSender と同じ作り)。実装は `AzureOpenAIEmbeddingProvider` と、Microsoft.Extensions.AI の生成器を包む `EmbeddingGeneratorProvider` (別プロバイダは IEmbeddingProvider を実装して対応表に足す)。テンプレートは appsettings の `SemanticSearch.EmbeddingProvider` の呼び名で対応表 (`EmbeddingProviderTable`) から選ぶ。空なら意味検索は無効 (文章だけ保存)。モデルを変えたら列の次元を合わせて作り直し、`Reindex()` で全行再索引

### 文章の規則 (SemanticSearchText)

- 対象フィールドを順に「表示名: 値」(値が空のフィールドは出さない)。表示名が無ければフィールド名
- 候補値 (SelectField) は表示名、リンクは表示文字列、日付は `yyyy-MM-dd`、日時は `yyyy-MM-dd HH:mm`、真偽は「はい / いいえ」、リッチテキストはタグを除いた本文、ファイルはファイル名。List / Module / Password / SemanticSearch 自身は出さない
- 改行は LF 固定。文章の列には全文を保存し、`MaxTextLength` を超えた分は埋め込みに渡さない (キーワード検索は全文に当たる)
- 作るのはサーバー (保存のたび・再索引・一括取込)。対象フィールドがフロントに読み込まれているかどうかは関係ない

### C# クラス定義 (真実の源)

外部ライブラリ `Codeer.LowCode.Blazor.Extras` で定義。`FieldDesignBase` を継承する。ランタイムの Field は UI も読み込みデータも持たず、Submit でも何も送らない。

### プロパティ

> 共通プロパティ（Name）は [_FieldCommon.md](_FieldCommon.md) を参照。IgnoreModification / OnValidateInput / IsFocusSkip / OnFocusMoving / NextFocusField はデザイナ非表示。

| プロパティ | 型 | デフォルト | 説明 |
|---|---|---|---|
| `SourceFields` | string[] | `[]` | 文章にする**同じモジュール内**のフィールド名。空なら DB カラムを持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成 / 更新の記録・このフィールド自身は除く)。 |
| `DbColumnText` | string | `""` | 文章を保存する DB カラム名。**書き込み専用・必須**。 |
| `DbColumnVector` | string | `""` | 埋め込みベクトル (`[0.1,-0.2,…]` の JSON 配列テキスト) を保存する DB カラム名。**書き込み専用・必須**。 |
| `DbColumnVectorSearch` | string | `""` | DB のベクトル検索で距離計算に使うベクトル型の列。**必須**。PostgreSQL は `DbColumnVector` をキャストする生成列の名前、SQL Server は `DbColumnVector` と同じ列名 (VECTOR 型にする)。 |
| `MaxTextLength` | int | `8000` | 埋め込みに渡す文章の最大文字数 (埋め込みモデルの入力上限の歯止め)。文章の列は全文。 |
| `SearchMaxDistance` | double? | `null` | 検索レイアウトの検索欄で探すときのコサイン距離の上限 (0〜2)。空なら距離では絞らない (並びだけ)。 |
| `KeywordSearch` | bool | `false` | 検索レイアウトの検索欄にキーワード欄も出す。空白区切りの語をすべて含む行 (文章の列への部分一致の AND) に絞る。埋め込み不要・どの DB でも動く。文章と両方入れると絞ったうえで近い順。 |
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

ベクトル検索に対応しない DB (SQLite / MySQL / Oracle) でキーワード検索だけ使うときも列は 3 つ必要。`DbColumnVectorSearch` は `DbColumnVector` と同じ列名 (文字列の列) でよい。

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

### 検索レイアウトに置く (画面の検索欄)

- 検索レイアウトに置くと文章の入力欄が出る。文章で一覧を絞れる (行の条件と、このフィールド自身の読み取り権限が一覧検索と同じに効く)
- **近い順にしたいときは、一覧の並び (ListField の `SearchCondition.SortConditions`) の先頭に `{ "Variable": "Search.Value", "IsDescending": false }` を入れる**。文章が空のときはこの並びは飛ばされ、次の並びになる
- 文章を埋め込みにするのはサーバー (ホストが `AddInterceptor(SemanticSearchIndex.Service.Interceptor)` を登録済みであること = 保存時の索引付けと同じインターセプタ。Cookie テンプレートは登録済み)
- `SearchMaxDistance` が空で、その検索レイアウトを使う一覧 (ページの一覧・SearchField の結果の一覧・LinkField の検索ダイアログ) の並びに `Search.Value` が無いと、文章を入れても索引のある行に絞られるだけで一覧は変わらない。デザインチェック `SemanticSearchFieldDesign:3` が指摘する (並びに入れるか、`SearchMaxDistance` を設定する。`KeywordSearch: true` なら指摘しない = キーワードだけで使う構成のため)
- **キーワード検索** (`KeywordSearch: true`): 検索欄にキーワード欄が加わる。品番・人名・番号など語そのもので探す用途 (意味検索は似た番号が混ざる・0 件にならない)。空白区切りの語をすべて含む行に絞る (対象フィールドを横断。リンクの表示名・タグ名も当たる)。埋め込みを使わないので SQLite / MySQL / Oracle のモジュールや埋め込みプロバイダの無い構成でも使える (列は 3 つとも必要。文章の欄も出るが、入れるとプロバイダ未設定 / DB 非対応のエラー)。AI チャットからは filters にこの項目の `Like` でキーワードを渡す (設定に関係なく使える)。一致の大文字小文字・全角半角は文章の列の照合順序次第。スクリプトで `KeywordText` を設定すれば欄が無くても条件に入る
- **読み取り権限**: 検索できるかはこのフィールド自身の読み取り権限で決まる (他のフィールドと同じ)。文章の列には対象フィールドの値がそのまま入るので、読ませたくない項目は `SourceFields` に入れない (リンク先・タグ付けモジュールの閲覧条件は見ない = そのモジュールを読めない人にも表示名で当たる)。外したら `Reindex()` (既存の行の文章に値が残る)。入れるなら、このフィールドにも同じ読み取り条件 (CurrentUser で決まるもの) を付ける。行の値で決まる条件の項目は `SourceFields` から外す (デザインチェック: 対象フィールドに読み取り条件があるのにこのフィールドに無い構成は `SemanticSearchFieldDesign:4`、行の値で決まる条件の項目は `:5`。`:4` は条件の有無だけを見る)。読み取り条件は CurrentUser を参照するものにする (検索条件の権限判定は行を持たないので、行データを参照する条件を付けるとこのフィールドでの検索は拒否される。デザインチェック `PermissionFieldDesign:4`)

### 注意

- 意味検索できるのは PostgreSQL (pgvector) と SQL Server 2025 のデータソースだけ。SQLite / MySQL / Oracle のモジュールは意味検索の対象にならない (キーワード検索 `KeywordSearch` は使える)
- 埋め込みモデルを変えたらベクトル列を作り直して全行の再索引が要る (次元が違うベクトルは入らない)
- 文章は AI プロバイダに送られる。個人情報などを入れたくないときは `SourceFields` で絞る
- `RawDataAccessAgent` の `search_records` だけは AI 用の DB 接続で直接読む: 読める範囲と接続は `RawDataAccessOptions.DataSourceNames` で決まり (モジュールのデータソースが一覧に無ければ、AI 用の別名接続のうち同じ DB 種別のもので同じ表を引く)、行ごとの DataReadCondition・項目の読み取り権限は効かない。置くページの UserReadCondition で使える人を絞る。画面の検索欄と `ModuleDataAccessAgent` は一覧検索なので行の条件・項目の権限が効く

## Script

| メンバー | 型 | 説明 |
|---|---|---|
| `Reindex()` | void | 全行の文章とベクトルを作り直す (埋め込みモデルを変えたとき・フィールドを後から置いたとき)。サーバーのジョブとして走る。走っている間は無視。 |
| `ReindexMissing()` | void | ベクトルがまだ無い行だけ索引を付ける (埋め込みに失敗した行の穴埋め)。 |
| `CancelReindex()` | void | 走っている再索引を中断する。 |
| `IsReindexing` | bool | 再索引が走っている間 true。 |
| `ReindexProcessed` | int | 書き直した行数 (走っている間は途中経過)。 |
| `ReindexTotal` | int | 対象の行数 (分かるまでは 0)。 |
| `ReindexError` | string | 最後の再索引のエラー (成功なら空。中断も文言が入る)。 |
| `SearchText` / `KeywordText` | string | 検索レイアウトの文章の欄 / キーワード欄の値 (スクリプトで設定すれば、欄を出していなくても条件に入る)。 |

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

誰が起こせるかは権限で決まる: 再索引は索引を書き直す操作なので、API はこのフィールドに今のユーザーがユーザー権限だけで書けるとき (アプリアクセス条件・モジュールの UserWriteCondition・PermissionField の書き込み条件) だけ受け付ける。どれも無い既定の構成ではアプリに入れる人全員が起こせる (全行の埋め込み = 従量課金) ので、管理者などに絞るにはこのフィールドを対象にした PermissionField の書き込み条件 (CurrentUser で決まるもの) を付ける (ボタンを置くページの UserReadCondition は画面を隠すだけで API は絞らない)。索引は保存時と同じくサーバーが権限を通さず全行を読んで書き込み専用の列だけに書く (誰が起こしても同じ文章。レコードの更新ではない = 更新日時・版・監査・編集履歴に乗らない)。

値・データ系メソッドは公開しない (`IsModified` は常に `false`。索引はサーバーが保存した行から作る)。
