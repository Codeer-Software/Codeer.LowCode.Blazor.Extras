## Design

AI (サーバー側の Agent) と会話するチャット UI フィールドです。ユーザーの発言を送り、返事を吹き出しで表示します。返事は HTML として表示されるので、表・コードブロック・リンク・見出しなど Agent が返した表現がそのまま見えます。AI の実体と会話の履歴はサーバー側の持ち物で、このフィールドは入力と表示だけを担います。

画面には返事の一覧、入力欄 (Enter で送信、Shift+Enter で改行)、送信ボタンが表示されます。返事を待つ間は「考え中」の点滅と途中経過 (Agent が報告した場合)、経過秒数が出て、停止ボタンで中断できます。

### 動作の流れ

1. ユーザーが入力して送信する
2. サーバーへ発言を送り、すぐに受付番号が返る (この時点で「考え中」を表示)
3. 受付番号で状態を定期的に問い合わせる。途中経過や「ここまでの返事」が返れば逐次表示する
4. 返事が確定したら吹き出しを確定し、`OnReplyReceived` に設定したスクリプトを呼ぶ (引数は返事の HTML)
5. 失敗したときはエラー表示と「再送」ボタン、`TimeoutSeconds` を超えたときは打ち切って案内を表示する

### デザイナー設定プロパティ

| プロパティ | 型 | 必須 | 説明 |
|---|---|---|---|
| Placeholder | string | - | 入力欄のプレースホルダ |
| WelcomeMessage | string (複数行, HTML 可) | - | 会話の先頭に表示するアシスタントの挨拶。空なら表示しない |
| Height | int | - | 高さ (px)。0 なら親の高さに合わせる。**`IsFillAvailable: true` のグリッドに置くか、Height を指定する** (どちらも無いと内容に合わせて伸び続ける) |
| TimeoutSeconds | int | - | 返事を待つ上限 (秒)。既定 600。超えたら問い合わせをやめてエラー表示にする (サーバー側の処理は止めない) |
| MaxInputRows | int | - | 入力欄が自動で伸びる上限の行数。既定 6 |
| OnReplyReceived | string (スクリプトイベント) | - | 返事が確定したときに呼ぶスクリプト。`void Xxx(string replyHtml)` |

### サーバー側設定が必須

このフィールドはサーバーのチャット API (テンプレートは `/api/ai_chat`) を呼び出します。サーバー側では `Codeer.LowCode.Blazor.Extras.Server` の `IAIChatAgent` を実装して返事を作り、`AIChatJobStore` と一緒に DI に登録します (Example の `AIChatController` / `Program.cs` を参照)。返事は Markdown・テキスト・HTML のどれで返してもよく、サーバーが HTML に揃えてから画面に届きます。

- Agent が未設定の環境 (デザイナのプレビュー等) では入力欄が無効になり、その旨を表示します
- 会話の履歴はサーバー側 (Agent) が conversationId で保持します。「新しい会話」で conversationId が振り直されます

### 注意事項

- このフィールド自体は DB に値を保存しません。返事を残したいときは `OnReplyReceived` で他のフィールドに入れて保存してください
- 返事の HTML はそのまま描画されます (サーバーは信頼する前提)。利用者の入力を HTML にして返さないよう Agent 側で注意してください
- ページを離れると問い合わせは止まりますが、サーバー側の処理は続きます (結果は一定時間保持されます)

## Script

### スクリプト API

| メンバ | 説明 |
|---|---|
| `Send(string text)` | 発言を送る (待ち中・空文字は無視) |
| `Cancel()` | 待ち中の返事を中断する |
| `Clear()` | 履歴を消して新しい会話にする |
| `IsBusy` | 返事を待っている間 true |
| `LastReply` | 最後に確定した返事 (HTML) |
| `ConversationId` | 現在の会話の識別子 |

### 使用例

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
