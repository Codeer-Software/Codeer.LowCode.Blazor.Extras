# AIChatField - AI チャット

AI (サーバー側の Agent) と会話するチャット UI フィールドです。ユーザーの発言を送り、返事を吹き出しで表示します。**返事は HTML をそのまま表示**するので、表・コードブロック・リンク・見出し・インラインスタイルなど、Agent が「見やすい」と判断した表現がそのまま見えます。

AI の実体と会話の履歴はサーバー側の持ち物で、このフィールドは入力と表示だけを担います。返事を作る Agent はサーバーが名前を付けて登録し、フィールドの `Agent` プロパティでどれを使うかを選びます。標準で `ChatClientAgent` (Microsoft.Extensions.AI の IChatClient で会話) と `RawDataAccessAgent` (DB を SQL で読んで集計・グラフで答える) を用意しています。サンプルアプリの既定はダミー Agent (AI 未接続) で、Azure OpenAI の設定があれば `RawDataAccess` も使えます。

## 機能

- **入力**: Enter で送信、Shift+Enter で改行 (IME の確定 Enter は送信しない)。入力欄は内容に合わせて伸びる (`MaxInputRows` まで)
- **考え中**: 送信直後にアシスタント側の吹き出しを出し、点滅ドット・途中経過 (Agent が報告した場合)・経過秒数を表示
- **逐次表示**: Agent が「ここまでの返事」を報告すれば、確定前から吹き出しの中身が更新される
- **停止**: 待ち中は送信ボタンが停止ボタンになり、サーバーに中断を伝える
- **エラーと再送**: 失敗した発言はエラー表示になり、同じ内容で再送できる
- **新しい会話**: 履歴を消して会話の識別子を振り直す
- **コピー**: 返事の吹き出しにマウスを乗せるとコピーボタンが出る (テキストとしてコピー)
- **高さ**: `IsFillAvailable: true` のグリッドに置くと残りの高さを使い切り、履歴だけが内部スクロールする。`Height` (px) の指定も可

## デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Agent | string | - | 返事を作るサーバー側 Agent の名前。空なら既定の Agent。サーバー側の対応表にある名前 (例: `RawDataAccess`) を指定し、同じフィールドで用途の違う Agent を使い分ける |
| Placeholder | string | - | 入力欄のプレースホルダ |
| WelcomeMessage | string (複数行, HTML 可) | - | 会話の先頭に表示するアシスタントの挨拶。空なら表示しない |
| Height | int | - | 高さ (px)。0 なら親の高さに合わせる。`IsFillAvailable` のグリッドに置くか Height を指定する |
| TimeoutSeconds | int | - | 返事を待つ上限 (秒)。既定 600。超えたら問い合わせをやめてエラー表示にする (サーバー側の処理は止めない) |
| MaxInputRows | int | - | 入力欄が自動で伸びる上限の行数。既定 6 |
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
POST   {EndPoint}               { "conversationId": "…", "message": "…", "agent": "" }   // agent = デザインの Agent 名 (空 = 既定)
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

`Codeer.LowCode.Blazor.Extras.Server` に部品があります。フォルダと名前空間は役割で分かれています: `AI/Chat/` (共有: 契約 `IAIChatAgent` と窓口 `AIChatJobStore`)、`AI/Chat/ChatClient/` (共有: 標準 Agent の基盤 `ChatClientAgent`)、`AI/Chat/RawDataAccess/` (専用: `RawDataAccessAgent` とその設定)。アプリに見える入口は、契約 `IAIChatAgent`、窓口 `AIChatJobStore`、標準 Agent 2 つ (`ChatClientAgent` / `RawDataAccessAgent`) だけです。アプリは「Agent 名 → Agent」の対応表 (メールの `MailSenderTable` と同じ位置づけの静的クラス) で Agent を持ち、AIChatField はデザインの `Agent` でその名前を指定します。DI は要りません。

| 型 | 役割 |
|---|---|
| `IAIChatAgent` | 返事を作る側のインターフェース。`ReplyAsync(request, progress, cancellationToken)` で `AIChatReply` (テキスト / Markdown / HTML / Auto) を返す。途中経過は `IAIChatProgress` に報告。`request.AgentName` にデザインの Agent 名が入る |
| `AIChatJobStore` | プロセス内のジョブ置き場。コンストラクタで対応表 (`Func<string, IAIChatAgent?>`。Agent が 1 つなら `IAIChatAgent` を直接) を受け、送信で Agent をバックグラウンド実行し、状態をポーリングに返す。`Start(owner, conversationId, message, agentName)` で Agent 名を渡す。対応表に無い名前は error になる。プロセスに 1 つ (アプリの静的プロパティ) |
| `ChatClientAgent` (+ `ChatClientAgentOptions`) | 標準 Agent。Microsoft.Extensions.AI の `IChatClient` で返事を作る素の会話 Agent (システムプロンプト、会話履歴 (会話 ID ごと・保持期限つき)、逐次表示、Markdown → HTML)。モデルの選択と認証はアプリが `Func<IChatClient>` で渡す。FAQ のような「知識だけで答える」用途 |
| `RawDataAccessAgent` (+ `RawDataAccessOptions`) | DB を直接読んで集計・グラフで答える Agent (`ChatClientAgent` を中に持って委譲)。内部に `get_schema` (表と列。モジュール定義があれば業務名を添える)、`execute_sql` (読み取り専用の SELECT を 1 文実行。行数・文字数・時間の上限、監査ログ)、`render_chart` (棒 / 折れ線 / 円。数値からサーバーで SVG を作るので数字が狂わない) のツールを持つ。依存 (IChatClient と IDbAccessor の作り方、モジュール定義) はコンストラクタ、設定 (`RawDataAccessOptions`: データソース名と上限) は別 |
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
            DesignerService.GetDesignData().Modules,                     // 表と列に業務名を添える (null 可)
            new RawDataAccessOptions { DataSourceName = "Analytics" }),  // AI 用の読み取り専用 DB ユーザーで接続するデータソース
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
        => Accepted(new AIChatSendResponse { RequestId = jobs.Start(Owner, request.ConversationId, request.Message, request.Agent) });

    [HttpGet("{requestId}")]
    public ActionResult<AIChatStatusResponse> Status(string requestId)
        => jobs.GetStatus(Owner, requestId) is { } s ? s : NotFound();

    [HttpDelete("{requestId}")]
    public IActionResult Cancel(string requestId)
        => jobs.Cancel(Owner, requestId) ? NoContent() : NotFound();
}
```

```csharp
// IChatClient の作り方はアプリの責務 (Example/Extras/Extras/Extras.Server/AI/AIChatClientFactory.cs。Azure OpenAI の例)
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
- そのユーザーで接続するデータソースを appsettings の `DataSources` / `ConnectionStrings` に追加し、`RawDataAccessOptions.DataSourceName` に指定する (Options は文字列と数値だけなので appsettings のセクションからそのまま束縛できる)。アプリ本体と同じ接続を渡すと DB 全体が読める
- SQLite はユーザーが無いので接続文字列の `Mode=ReadOnly` で読み取り専用にする (表・列の限定はできない)
- ログインユーザーごとの行制限 (モジュールの UserRead / DataRead 条件) は効かない。「誰がこのチャットを使えるか」は、フィールドを置くページやモジュールの UserReadCondition で絞る

`execute_sql` 側の SELECT 判定 (1 文だけ・INSERT/UPDATE/DELETE 等の語を含まない) は補助で、書き込み拒否の本体は DB ユーザーの権限です。行数 (`MaxRows` 既定 200)、文字数 (`MaxResultChars` 既定 20000)、タイムアウト (`CommandTimeoutSeconds` 既定 30) の上限と、実行した SQL のログ (`ChatClientAgentOptions.LoggerFactory` を設定したとき) はツール側が担います。

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
- `AIChatJobStore` と `ChatClientAgent` の会話履歴は単一インスタンス前提のメモリ保持です。サーバーを複数インスタンスにするときはセッション固定 (Azure App Service の ARR アフィニティは既定でオン) が前提で、それが使えない構成では共有ストア (DB テーブル) への置き換えが必要です
- 所有者 (ログイン ID) が一致しないジョブは見えません。匿名同士は共有になるので、認証のあるアプリで使ってください

## 注意事項

- このフィールド自体は DB に値を保存しません。返事を残したいときは `OnReplyReceived` で他のフィールドに入れて保存してください
- 返事の HTML はそのまま描画されます (サーバーは信頼する前提)。利用者の入力をそのまま HTML にして返さないよう Agent 側で注意してください
- ページを離れると問い合わせは止まりますが、サーバー側の処理は続きます (結果は既定で 30 分保持)
- Agent が未設定の環境 (デザイナのプレビュー等) では入力欄が無効になり、その旨を表示します
