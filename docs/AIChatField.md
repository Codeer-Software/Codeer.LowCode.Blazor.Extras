# AIChatField - AI チャット

AI (サーバー側の Agent) と会話するチャット UI フィールドです。ユーザーの発言を送り、返事を吹き出しで表示します。**返事は HTML をそのまま表示**するので、表・コードブロック・リンク・見出し・インラインスタイルなど、Agent が「見やすい」と判断した表現がそのまま見えます。

AI の実体と会話の履歴はサーバー側の持ち物で、このフィールドは入力と表示だけを担います。返事を作る Agent はサーバーが名前を付けて登録し、フィールドの `Agent` プロパティでどれを使うかを選びます。標準で `RawDataAccessAgent` (DB を SQL で読んで集計・グラフで答える) を用意しています。サンプルアプリの既定はダミー Agent (AI 未接続) で、Azure OpenAI の設定があれば `RawDataAccess` も使えます。

## 機能

- **入力**: Ctrl+Enter は常に送信、Shift+Enter は常に改行。Enter は `SendOnEnter` (既定 true) で送信か改行かを選べる (IME の確定 Enter は送信しない)。入力欄は `MinInputRows` (既定 3) 行ぶんの高さがあり、内容に合わせて 12 行まで伸びる
- **考え中**: 送信直後にアシスタント側の吹き出しを出し、点滅ドット・途中経過 (Agent が報告した場合)・経過秒数を表示
- **逐次表示**: Agent が「ここまでの返事」を報告すれば、確定前から吹き出しの中身が更新される
- **停止**: 待ち中は送信ボタンが停止ボタンになり、サーバーに中断を伝える
- **エラーと再送**: 失敗した発言はエラー表示になり、同じ内容で再送できる
- **新しい会話**: 履歴を消して会話の識別子を振り直す
- **コピー**: 返事の吹き出しにマウスを乗せるとコピーボタンが出る (テキストとしてコピー)
- **会話は画面を離れても残る**: 会話 (発言・返事・会話 ID・待ち中の返事) はアプリのメモリ上の保管庫 (DI スコープごと = WASM ならタブ) にモジュール名 + フィールド名で置かれ、返事の中のリンクで別ページへ行って戻っても消えない。返事待ちのポーリングは画面から離れている間も続くので、戻った瞬間に届いた返事が見える。リロードか「新しい会話」で消える (`KeepConversation` を false にすると画面を離れた時点で消える)
- **高さ**: `IsFillAvailable: true` のグリッドの最終行に置くと残りの高さを使い切り、履歴だけが内部スクロールする。普通の行では履歴の領域は 16rem 固定で、返事が増えても行は伸びない (行が伸びると下の行やページを押し出す)。決まった高さにしたいときは行の Height を使う

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Agent | string | - | 返事を作るサーバー側 Agent の名前。空なら既定の Agent。サーバー側の対応表にある名前 (例: `RawDataAccess`) を指定し、同じフィールドで用途の違う Agent を使い分ける |
| DocumentFolder | string | - | この会話に渡す補足文書のフォルダ (デザインプロジェクトの `Resources` からの相対パス。例: `AIChat/Sales`)。そのフォルダの `.md` / `.txt` が用語の定義や集計の決まりとして Agent に渡る。空なら文書なし。チャットごとに別のフォルダを指定して、用途に合った文書だけを渡す (トークンの節約にもなる) |
| KeepConversation | bool | - | 画面を離れても会話を保持するか。既定 true。会話はアプリのメモリ (WASM ならブラウザのタブ) に置かれ、返事のリンクで別ページへ行って戻っても消えず、待ち中の返事も届く。リロードか「新しい会話」で消える。false なら画面を離れた時点で消える |
| TimeoutSeconds | int | - | 返事を待つ上限 (秒)。既定 600。超えたら問い合わせをやめてエラー表示にする (サーバー側の処理は止めない) |
| MinInputRows | int | - | 入力欄の行数 (最小)。既定 3。内容が増えれば 12 行まで自動で伸び、それ以上は入力欄の中でスクロール |
| SendOnEnter | bool | - | Enter キーで送信するか。既定 true。false なら Enter は改行。Shift+Enter (改行) と Ctrl+Enter (送信) はこの設定に関係なく固定 |
| OnReplyReceived | string (スクリプトイベント) | - | 返事が確定したときに呼ぶスクリプト。`void Xxx(string replyHtml)` |

## スクリプト API

| メンバ | 説明 |
|---|---|
| `Send(string text)` | 発言を送る (待ち中・空文字は無視) |
| `Cancel()` | 待ち中の返事を中断する |
| `Clear()` | 履歴を消して新しい会話にする |
| `IsBusy` | 返事を待っている間 true |
| `LastReply` | 最後に確定した返事 (HTML) |
| `ConversationId` | 現在の会話の識別子 |

```csharp
// 画面の内容を前置きして質問する
void AskButton_OnClick()
{
    Chat.Send("次の受注について納期の目安を教えてください。\n顧客: " + Customer.Value + "\n数量: " + Quantity.Value);
}

// OnReplyReceived に "Chat_OnReplyReceived" を設定した場合
void Chat_OnReplyReceived(string replyHtml)
{
    Memo.Value = replyHtml;
}
```

## 通信 (サーバー側の API)

クライアントは HTML を表示するだけで内容を解釈しません。Markdown やテキストを HTML にするのはサーバーの仕事です。返事に時間がかかる Agent を想定し、HTTP を張ったまま待たず、受付番号でポーリングします。

```
POST   {EndPoint}               { "conversationId": "…", "message": "…", "agent": "", "documentFolder": "" }   // agent = デザインの Agent 名 (空 = 既定)、documentFolder = 補足文書のフォルダ (空 = なし)
       → 202 { "requestId": "…" }
GET    {EndPoint}/{requestId}
       → { "status": "running" | "done" | "error" | "canceled",
           "reply": "<p>…</p>",      // running 中は「ここまでの HTML」(任意)、done で確定
           "progress": "検索しています…",  // 任意
           "error": "…" }
DELETE {EndPoint}/{requestId}   // 中断
```

- ポーリングの間隔は最初の 10 秒は 1 秒、以後 2.5 秒
- 会話の履歴は `conversationId` を鍵にサーバー (Agent) が持ちます。クライアントは全履歴を送りません
- やり取りする型は `Codeer.LowCode.Blazor.Extras.AIChat` 名前空間 (`AIChatSendRequest` / `AIChatSendResponse` / `AIChatStatusResponse`)

## サーバー側の実装

`Codeer.LowCode.Blazor.Extras.Server` に部品があります。フォルダと名前空間は役割で分かれています: `AI/Chat/` (共有: 契約 `IAIChatAgent` と窓口 `AIChatJobStore`)、`AI/Chat/ChatClient/` (内部: 会話エンジン `ChatClientAgent`。公開しない)、`AI/Chat/RawDataAccess/` (専用: `RawDataAccessAgent` とその設定)。アプリに見える入口は、契約 `IAIChatAgent`、窓口 `AIChatJobStore`、標準 Agent 2 つ (`ChatClientAgent` / `RawDataAccessAgent`) だけです。アプリは「Agent 名 → Agent」の対応表 (メールの `MailSenderTable` と同じ位置づけの静的クラス) で Agent を持ち、AIChatField はデザインの `Agent` でその名前を指定します。DI は要りません。

| 型 | 役割 |
|---|---|
| `IAIChatAgent` | 返事を作る側のインターフェース。`ReplyAsync(request, progress, cancellationToken)` で `AIChatReply` (テキスト / Markdown / HTML / Auto) を返す。途中経過は `IAIChatProgress` に報告。`request.AgentName` にデザインの Agent 名が入る |
| `AIChatJobStore` | プロセス内のジョブ置き場。コンストラクタで対応表 (`Func<string, IAIChatAgent?>`。Agent が 1 つなら `IAIChatAgent` を直接) を受け、送信で Agent をバックグラウンド実行し、状態をポーリングに返す。`Start(owner, conversationId, message, agentName, documentFolder)` で Agent 名と文書フォルダを渡す。対応表に無い名前は error になる。プロセスに 1 つ (アプリの静的プロパティ) |
| `RawDataAccessAgent` (+ `RawDataAccessOptions`) | アプリの設計を読み、DB を直接読んで集計・グラフで答える Agent (会話の基盤は内部の会話エンジン: モデル呼び出し、会話履歴、逐次表示、Markdown → HTML。履歴の鍵は「ログイン ID + 会話 ID」(認証の無いアプリでは会話 ID だけ) で保持期限つき。トークンの膨張は `RawDataAccessOptions` の `KeepToolResultsForTurns` / `MaxHistoryTurns` / `MaxHistoryCharacters` の 3 段で抑える)。内部のツール: `list_modules` / `describe_module` (モジュール定義: フィールドの表示名・型・DB 列、候補値 (コード=名称)、リンク (結合相手とキー)、論理削除、スクリプト)、`read_document` (補足文書)、画面 URL (一覧 `/{フレーム}/{セグメント}`・詳細 `/{フレーム}/{セグメント}/{Id}` をページフレームのリンクから組み、行を挙げる返事に「開く」リンクを付ける)、`get_schema` (引数なしなら表名の目次だけ、`tables` を指定した表の列だけを 1 表 1 行で。方言はシステムプロンプトに常時入っていて、設計で表と列が分かるときは呼ばない指示にしている = トークン節約)、`execute_sql` (指定データソースで読み取り専用 SELECT を 1 文。行数・文字数・時間の上限、監査ログ)、`render_chart` (棒 / 折れ線 / 円。サーバーで SVG を作るので数字が狂わない)。依存 (IChatClient と IDbAccessor の作り方、デザイン定義、文書) はコンストラクタ、設定 (`RawDataAccessOptions`: データソース名の一覧と上限) は別 |
| `AIChatDocument` | Agent に渡す補足文書 (名前と本文)。出所はホストが決める。標準はデザインプロジェクトの `Resources/{DocumentFolder}/*.md` (フォルダはフィールドの `DocumentFolder`) |
| (内部) HTML 化 | 返事は `AIChatJobStore` の中で HTML に揃えられる。Markdown は [Markdig](https://github.com/xoofx/markdig) (表・タスクリスト・自動リンク、単独改行は `<br>`)、テキストはエスケープ、HTML は素通し (リンクに `target="_blank"` を付けるだけ)。Agent 側で HTML 化のコードを書く必要はない |

Example の `DummyAIChatAgent` (`Example/Extras/Extras/Extras.Server/AI/`) は AI を呼ばないダミーで、UI の確認と、自分で `IAIChatAgent` を書くときの雛形です。発言に `html` / `text` / `error` / `slow` を含めると振る舞いが変わります。

### 対応表と Controller (アプリ側)

対応表と Controller はアプリの持ち物です。サンプル (`Example/Extras/Extras/Extras.Server/AI/AIChatAgentTable.cs` と `Controllers/AIChatController.cs`):

```csharp
// AI/AIChatAgentTable.cs: Agent 名 → Agent。AIChatField のデザインの Agent がこの名前を指す。自分の Agent はここに 1 行足す
public static class AIChatAgentTable
{
    static readonly ConcurrentDictionary<string, Lazy<IAIChatAgent?>> _agents = new(StringComparer.OrdinalIgnoreCase);

    public static AIChatJobStore Jobs { get; } = new(Create);   // プロセスに 1 つ

    public static IAIChatAgent? Create(string name)              // 会話履歴を持つので名前ごとに 1 つを使い回す
        => _agents.GetOrAdd(name ?? "", n => new Lazy<IAIChatAgent?>(() => CreateCore(n))).Value;

    static IAIChatAgent? CreateCore(string name) => name switch
    {
        "" => new DummyAIChatAgent(),                              // 既定 (Agent が空のフィールド)
        "RawDataAccess" => new RawDataAccessAgent(
            chatClientFactory,                                           // IChatClient の作り方 (下記)
            () => new DbAccessor(SystemConfig.Instance.DataSources),     // IDbAccessor の作り方 (SQL ごとに作って捨てる)
            () => DesignerService.GetDesignData(),                       // デザイン定義 (ホットリロードで変わるので都度。null 可)
            folder => DesignDataFileManager.GetResourceTexts(designFileDirectory, folder, ".md", ".txt")   // 補足文書 = デザインの Resources/{DocumentFolder}/*.md|*.txt (本体 1.3.32 以降。null 可)
                          .Select(e => new AIChatDocument(e.Name, e.Text)).ToList(),
            new RawDataAccessOptions { DataSourceNames = { "Analytics" } }),  // AI 用の読み取り専用 DB ユーザーで接続するデータソース (複数可)
        _ => null,                                                   // 知らない名前 → ジョブは error
    };
}
```

```csharp
[ApiController]
[Route("api/ai_chat")]
public class AIChatController : ControllerBase
{
    static AIChatJobStore jobs => AIChatAgentTable.Jobs;

    string Owner => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? string.Empty;

    [HttpPost]
    public ActionResult<AIChatSendResponse> Send([FromBody] AIChatSendRequest request)
        => Accepted(new AIChatSendResponse { RequestId = jobs.Start(Owner, request.ConversationId, request.Message, request.Agent, request.DocumentFolder) });

    [HttpGet("{requestId}")]
    public ActionResult<AIChatStatusResponse> Status(string requestId)
        => jobs.GetStatus(Owner, requestId) is { } s ? s : NotFound();

    [HttpDelete("{requestId}")]
    public IActionResult Cancel(string requestId)
        => jobs.Cancel(Owner, requestId) ? NoContent() : NotFound();
}
```

```csharp
// IChatClient の作り方はアプリの責務 (Example の AIChatAgentTable.CreateAzureOpenAI。Azure OpenAI の例)
var client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(key));
Func<IChatClient> chatClientFactory = () => client.GetChatClient(model).AsIChatClient();   // Microsoft.Extensions.AI.OpenAI
```

```csharp
// クライアント (ServiceInitializer)。URL はアプリの持ち物なので起動時に一度設定する
AIChatField.EndPoint = "/api/ai_chat";
```

### RawDataAccessAgent と DB の権限

`RawDataAccessAgent` は名前どおり DB を生で読みます (SELECT を組んで実行し、結果を表とグラフにする)。**何が読めるかは DB 側で決めます**:

- AI 用に読み取り専用の DB ユーザーを作り、見せてよい表・列だけに SELECT を GRANT する。ログイン情報 (パスワードハッシュ・TOTP シークレット等)、変更履歴、一時ファイルの表は GRANT しない
- そのユーザーで接続するデータソースを appsettings の `DataSources` / `ConnectionStrings` に追加し、`RawDataAccessOptions.DataSourceNames` に指定する (複数可。1 つの SQL は 1 つのデータソースにしか届かず、またがる質問は AI がデータソースごとに問い合わせて突き合わせる)。Options は文字列と数値だけなので appsettings のセクションからそのまま束縛できる。アプリ本体と同じ接続を渡すと DB 全体が読める
- SQLite はユーザーが無いので接続文字列の `Mode=ReadOnly` で読み取り専用にする (表・列の限定はできない)
- ログインユーザーごとの行制限 (モジュールの UserRead / DataRead 条件) は効かない。「誰がこのチャットを使えるか」は、フィールドを置くページやモジュールの UserReadCondition で絞る

`execute_sql` 側の SELECT 判定 (1 文だけ・INSERT/UPDATE/DELETE 等の語を含まない) は補助で、書き込み拒否の本体は DB ユーザーの権限です。行数 (`MaxRows` 既定 200)、文字数 (`MaxResultChars` 既定 20000)、タイムアウト (`CommandTimeoutSeconds` 既定 30) の上限と、実行した SQL のログ (`RawDataAccessAgent` のコンストラクタの `ILoggerFactory` を設定したとき) はツール側が担います。

**権限管理はライブラリではなく DB 側の設定と接続文字列で行ってください。** `RawDataAccessAgent` は「渡された接続で読めるものは読む」だけで、表や列の許可・不許可を判断する仕組みを持ちません。AI 用の DB ユーザー (またはビュー) を用意し、そのユーザーで接続するデータソースを appsettings に書く、が正式な手順です。

### AI に送られるデータ

`execute_sql` の結果 (行と列の値) は、そのまま AI のモデル (Azure OpenAI 等、アプリが `IChatClient` として渡したもの) に送られます。設計の情報 (モジュール名・列名・候補値) と補足文書も送られます。したがって、AI 用 DB ユーザーが読める列の中身は AI サービスへ出る、と考えてください。

多くの業務データ (受注、在庫、案件の数字) は、社内で SaaS を使うのと同じ扱いで問題になりません。Azure OpenAI は入力を学習に使わず、デプロイしたリージョン内で処理されます (不正利用監視のためにプロンプトと応答が一定期間保存される仕組みがあり、これは申請でオプトアウトできます)。導入先に説明するときは「学習に使われない」「リージョン (東日本など) 内で処理」「監視ログのオプトアウト」の 3 点を伝えると整理しやすいです。

気にする必要があるのは次の場合です。

- 個人 (消費者) の情報を含む表 (氏名、住所、電話、メール等)
- 顧客との契約で再委託先や処理場所が縛られている場合
- 業法で扱いが決まっている業種 (医療、金融など)

これらに当たる場合の手段も DB 側です。

1. **列単位で GRANT を切る。** 顧客マスタは ID と取引先名だけ SELECT を許し、住所・電話・メール・担当者名は許可しない (PostgreSQL / SQL Server / MySQL は列単位の GRANT が可能。Oracle はビューで代用)
2. **ビューをかませる。** AI 用に個人情報の列を落とした (またはマスクした) ビューを作り、AI ユーザーにはビューだけを見せる。運用上いちばん説明しやすい方法です
3. **送る先を変える。** どうしても外に出せない場合は、`IChatClient` を閉域 (Private Endpoint) の Azure OpenAI やローカルのモデル (Ollama 等) に差し替える。ライブラリは `IChatClient` 抽象しか知らないので Agent 側の変更は不要です

行数や文字数の上限はトークンの節約のためのもので、漏えい防止の手段ではありません。プロンプトで「個人情報を列挙しない」と指示するのも振る舞いを整える程度の効果です。DB から読める時点でデータは送られているので、対策は必ず DB 側で行ってください。実行した SQL は監査ログに全文残るので、「何が送られたか」は後から追えます。

### 設計と補足文書を AI に渡す

業務の意味は DB スキーマではなくデザインプロジェクトにあります。`RawDataAccessAgent` はデザイン定義 (`DesignData`) を受け取り、モデルに「業務語はまずモジュール定義で確かめ、表と列は describe_module で確認してから SQL を書く」よう指示します。モジュール定義に書けないこと (用語の定義、集計の決まり、データの見方) は、デザインプロジェクトの `Resources` の下のフォルダに Markdown で置き、フィールドの `DocumentFolder` でそのフォルダを指定します (例: `Resources/AIChat/Sales/` に置いて `DocumentFolder` = `AIChat/Sales`)。チャットごとに別のフォルダを指せるので、売上分析のチャットには売上の説明書だけ、在庫のチャットには在庫の説明書だけ、と分けられます。App.zip に入ってデプロイで反映されます。文書の合計が小さい (既定 8000 文字以下) うちは全文がシステムプロンプトに入り、大きくなると一覧と冒頭の抜粋だけが入って AI が `read_document` で必要なものを読みます (App.zip からの読み出しは Codeer.LowCode.Blazor 1.3.32 以降の `DesignDataFileManager.GetResourceTexts` で行います)。用語の定義 (「売上」は完了分だけ、など) は列名からは分からないので、ここに書くと答えの精度が目に見えて変わります。

### Agent を実装する

標準 Agent で足りないとき (独自のツールを持たせたい、モデルを使わない、独自の対話制御が要る) は `IAIChatAgent` を直接実装し、対応表に 1 行足します。モデルを呼ぶ部分は Microsoft.Extensions.AI をそのまま使えます (ツール呼び出しの往復は `FunctionInvokingChatClient` が肩代わりします):

```csharp
public class MyAgent : IAIChatAgent
{
    public async Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken token)
    {
        progress.Report("検索しています…");
        var result = await SearchAsync(request.Message, token);

        progress.Report("まとめています…");
        var markdown = await SummarizeAsync(result, token);   // LLM の出力 (Markdown)
        return AIChatReply.Markdown(markdown);                 // HTML にしたければ AIChatReply.Html(...)
    }
}
```

- 逐次表示したいときは、LLM のストリームを受けながら `progress.ReportPartial(AIChatReply.Markdown(ここまでの全文))` を呼ぶ (差分ではなく全体を渡す)
- サーバー側の会話履歴 (既定 2 時間) やジョブ (完了後 30 分) が消えたあとに続きを送ると、クライアントは表示中の会話の写し (テキストのみ・直近 6 往復・4000 文字まで、`AIChatSendRequest.Transcript`) を一緒に送り、`RawDataAccessAgent` は履歴が無いときだけそれで文脈を復元する。SQL の結果などツールの結果は写しに無いので、「その中で」のような直前の結果を指す追問は履歴が消えた後は精度が落ちる
- `AIChatJobStore` のジョブと Agent の会話履歴は単一インスタンス前提のメモリ保持です。サーバーを複数インスタンスにするときはセッション固定 (Azure App Service の ARR アフィニティは既定でオン) が前提で、それが使えない構成では共有ストア (DB テーブル) への置き換えが必要です
- 所有者 (ログイン ID) が一致しないジョブは見えません。匿名同士は共有になるので、認証のあるアプリで使ってください

## 注意事項

- このフィールド自体は DB に値を保存しません。返事を残したいときは `OnReplyReceived` で他のフィールドに入れて保存してください
- 返事の HTML はそのまま描画されます (サーバーは信頼する前提)。利用者の入力をそのまま HTML にして返さないよう Agent 側で注意してください
- ページを離れると問い合わせは止まりますが、サーバー側の処理は続きます (結果は既定で 30 分保持)
- Agent が未設定の環境 (デザイナのプレビュー等) では入力欄が無効になり、その旨を表示します
