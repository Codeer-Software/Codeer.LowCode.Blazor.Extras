# AIChatField - AI チャット

AI (サーバー側の Agent) と会話するチャット UI フィールドです。ユーザーの発言を送り、返事を吹き出しで表示します。**返事は HTML をそのまま表示**するので、表・コードブロック・リンク・見出し・インラインスタイルなど、Agent が「見やすい」と判断した表現がそのまま見えます。

AI の実体と会話の履歴はサーバー側の持ち物で、このフィールドは入力と表示だけを担います。サーバー側は `Codeer.LowCode.Blazor.Extras.Server` の `IAIChatAgent` を実装して返事を作ります (サンプルアプリはダミー Agent で、AI には接続していません)。

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
POST   {EndPoint}               { "conversationId": "…", "message": "…" }
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

`Codeer.LowCode.Blazor.Extras.Server` (名前空間 `Codeer.LowCode.Blazor.Extras.Server.AI.Chat`) に部品があります。

| 型 | 役割 |
|---|---|
| `IAIChatAgent` | 返事を作る側のインターフェース。`ReplyAsync(request, progress, cancellationToken)` で `AIChatReply` (テキスト / Markdown / HTML / Auto) を返す。途中経過は `IAIChatProgress` に報告 |
| `AIChatJobStore` | プロセス内のジョブ置き場。送信で Agent をバックグラウンド実行し、状態をポーリングに返す。シングルトンで登録する |
| `ChatReplyHtml` | 返事を HTML に揃える。Markdown は [Markdig](https://github.com/xoofx/markdig) (表・タスクリスト・自動リンク、単独改行は `<br>`)、テキストはエスケープ、HTML は素通し (リンクに `target="_blank"` を付けるだけ) |
| `DummyAIChatAgent` | AI を呼ばないダミー。発言に `html` / `text` / `error` / `slow` を含めると振る舞いが変わる |

### Controller (アプリ側)

Controller はアプリの持ち物です。サンプル (`Example/Extras/Extras/Extras.Server/Controllers/AIChatController.cs`):

```csharp
[ApiController]
[Route("api/ai_chat")]
public class AIChatController(AIChatJobStore jobs) : ControllerBase
{
    string Owner => User.Identity?.Name ?? string.Empty;

    [HttpPost]
    public ActionResult<AIChatSendResponse> Send([FromBody] AIChatSendRequest request)
        => Accepted(new AIChatSendResponse { RequestId = jobs.Start(Owner, request.ConversationId, request.Message) });

    [HttpGet("{requestId}")]
    public ActionResult<AIChatStatusResponse> Status(string requestId)
        => jobs.GetStatus(Owner, requestId) is { } s ? s : NotFound();

    [HttpDelete("{requestId}")]
    public IActionResult Cancel(string requestId)
        => jobs.Cancel(Owner, requestId) ? NoContent() : NotFound();
}
```

```csharp
// Program.cs
builder.Services.AddSingleton<IAIChatAgent, DummyAIChatAgent>();   // 本番は自前の実装に差し替える
builder.Services.AddSingleton<AIChatJobStore>();
```

```csharp
// クライアント (ServiceInitializer)。URL はアプリの持ち物なので起動時に一度設定する
AIChatField.EndPoint = "/api/ai_chat";
```

### Agent を実装する

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
- `AIChatJobStore` は単一インスタンス前提のメモリ保持です。スケールアウトするなら ARR アフィニティか共有ストアへの置き換えが必要です
- 所有者 (ログイン名) が一致しないジョブは見えません。匿名同士は共有になるので、認証のあるアプリで使ってください

## 注意事項

- このフィールド自体は DB に値を保存しません。返事を残したいときは `OnReplyReceived` で他のフィールドに入れて保存してください
- 返事の HTML はそのまま描画されます (サーバーは信頼する前提)。利用者の入力をそのまま HTML にして返さないよう Agent 側で注意してください
- ページを離れると問い合わせは止まりますが、サーバー側の処理は続きます (結果は既定で 30 分保持)
- Agent が未設定の環境 (デザイナのプレビュー等) では入力欄が無効になり、その旨を表示します
