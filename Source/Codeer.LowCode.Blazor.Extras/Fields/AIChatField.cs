using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>チャットの 1 発言。</summary>
    public class AIChatMessage
    {
        public bool IsUser { get; init; }
        /// <summary>ユーザー発言のテキスト (改行保持でそのまま表示)。</summary>
        public string Text { get; init; } = string.Empty;
        /// <summary>アシスタント発言の HTML。running 中は途中経過。</summary>
        public string Html { get; set; } = string.Empty;
        /// <summary>途中経過の一言。</summary>
        public string Progress { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public bool IsRunning { get; set; }
        public DateTime Time { get; init; } = DateTime.Now;
        /// <summary>サーバーのジョブ ID (アシスタント発言のみ)。</summary>
        public string RequestId { get; set; } = string.Empty;
        /// <summary>この発言の元になったユーザー発言 (再送用)。</summary>
        public string SourceText { get; init; } = string.Empty;
        public bool IsError => !string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// AI チャット UI のランタイム。会話の履歴と AI の実体はサーバー側にあり、ここは
    /// 「送信 → requestId → ポーリング → HTML を表示」だけを担う (<see cref="AIChatSendRequest"/> 参照)。
    /// <para>
    /// 会話そのもの (<see cref="AIChatConversation"/>) はデザインの KeepConversation (既定 true) なら DI スコープ (WASM ならタブ) ごとのメモリ上の保管庫に置き、
    /// 返事の中のリンクで別ページへ行って戻ってきても消えない (リロードで消える)。返事待ちのポーリングも会話側で続くので、離れている間に届いた返事は戻った瞬間に見える。
    /// サーバー側の履歴 (会話 ID ごと・保持期限あり) が消えていたときは、送信時に付ける会話の写し (<see cref="AIChatSendRequest.Transcript"/>) で Agent が文脈を取り戻す。
    /// </para>
    /// </summary>
    public class AIChatField(AIChatFieldDesign design) : FieldBase<AIChatFieldDesign>(design)
    {
        /// <summary>
        /// チャット API のベース URL (POST {EndPoint} / GET・DELETE {EndPoint}/{requestId})。
        /// URL はアプリ (Controller を持つ側) の持ち物なので起動時に一度設定する (テンプレートは "/api/ai_chat")。
        /// </summary>
        [ScriptHide]
        public static string EndPoint { get; set; } = string.Empty;

        /// <summary>
        /// ホスト側フック。設定されていれば (デスクトップアプリ等) HTTP を使わず直接処理する。
        /// 途中経過は IProgress に報告し、確定した状態を返す。
        /// </summary>
        [ScriptHide]
        public static Func<AIChatField, AIChatSendRequest, IProgress<AIChatStatusResponse>, CancellationToken, Task<AIChatStatusResponse>>? SendCoreAsync { get; set; }

        AIChatConversation? _conversation;
        int _version;

        AIChatConversation Conversation
        {
            get
            {
                if (_conversation == null)
                {
                    //保管庫は DI スコープ (Services) ごと。ホストの登録なしでスコープと同じ寿命になる
                    _conversation = Design.KeepConversation ? AIChatConversationStore.For(Services).GetOrCreate(ConversationKey) : new AIChatConversation();
                    Attach();
                }
                return _conversation;
            }
        }

        [ScriptHide]
        public IReadOnlyList<AIChatMessage> Messages => Conversation.Messages;

        /// <summary>表示内容が変わるたびに増える。コンポーネントが自動スクロールの判定に使う。</summary>
        [ScriptHide]
        public int Version => _version + Conversation.Version;

        /// <summary>会話の識別子。<see cref="ClearAsync"/> で振り直す。</summary>
        public string ConversationId => Conversation.ConversationId;

        /// <summary>返事を待っている間 true。</summary>
        public bool IsBusy => Conversation.IsBusy;

        /// <summary>最後に確定した返事 (HTML)。まだ無ければ空。</summary>
        public string LastReply => Conversation.LastReply;

        /// <summary>設定済みで送信できるか (エンドポイントかホストフックのどちらかがある)。</summary>
        [ScriptHide]
        public bool IsConfigured => SendCoreAsync != null || (Http != null && !string.IsNullOrEmpty(EndPoint));

        /// <summary>保管庫の鍵 (モジュール名 + フィールド名)。同じ画面に戻ったときに同じ会話が戻る。</summary>
        [ScriptHide]
        public string ConversationKey => $"{Module?.Design?.Name}:{Design.Name}";

        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        //ホスト (デザイナ等) には登録されていないことがあるため任意解決
        IHttpService? Http => Services.Provider?.GetService<IHttpService>();

        AIChatConversation.SendContext Context => new()
        {
            Field = this, Http = Http, EndPoint = EndPoint,
            Agent = Design.Agent ?? string.Empty, DocumentFolder = Design.DocumentFolder ?? string.Empty, TimeoutSeconds = Design.TimeoutSeconds,
        };

        /// <summary>発言を送る。待ち中や空文字は無視。返事はポーリングで受け取り <see cref="LastReply"/> と OnReplyReceived に届く。</summary>
        [ScriptName("Send")]
        public async Task SendAsync(string text)
        {
            if (!IsConfigured) return;
            await Conversation.SendAsync(text, Context);
        }

        /// <summary>待ち中の返事を中断する。サーバーにも中断を伝える。</summary>
        [ScriptName("Cancel")]
        public Task CancelAsync() => Conversation.CancelAsync(Context);

        /// <summary>履歴を消して新しい会話にする (待ち中なら中断する)。</summary>
        [ScriptName("Clear")]
        public Task ClearAsync() => Conversation.ClearAsync(Context);

        /// <summary>失敗した発言を同じ内容でもう一度送る (失敗した 2 件を取り除いてから送る)。</summary>
        [ScriptHide]
        public Task RetryAsync(AIChatMessage failed) => Conversation.RetryAsync(failed, Context);

        /// <summary>
        /// 画面から外れるときにコンポーネントが呼ぶ。会話への通知を切り、KeepConversation でなければ待ち中の返事も中断する
        /// (保管庫に置かない会話は表示と一緒に捨てる)。
        /// </summary>
        [ScriptHide]
        public async Task DetachAsync()
        {
            var conversation = _conversation;
            if (conversation == null) return;
            if (conversation.Changed == Touch) conversation.Changed = null;
            conversation.ReplyReceived = null;
            if (!Design.KeepConversation) await conversation.CancelAsync(Context);
        }

        void Attach()
        {
            _conversation!.Changed = Touch;
            _conversation.ReplyReceived = html => Module.ExecuteScriptAsync(Design.OnReplyReceived, html);
        }

        void Touch()
        {
            _version++;
            NotifyStateChanged();
        }
    }
}
