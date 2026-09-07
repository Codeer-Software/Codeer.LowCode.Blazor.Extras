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
        public DateTime Time { get; } = DateTime.Now;
        /// <summary>サーバーのジョブ ID (アシスタント発言のみ)。</summary>
        public string RequestId { get; set; } = string.Empty;
        /// <summary>この発言の元になったユーザー発言 (再送用)。</summary>
        public string SourceText { get; init; } = string.Empty;
        public bool IsError => !string.IsNullOrEmpty(Error);
    }

    /// <summary>
    /// AI チャット UI のランタイム。会話の履歴と AI の実体はサーバー側にあり、ここは
    /// 「送信 → requestId → ポーリング → HTML を表示」だけを担う (<see cref="AIChatSendRequest"/> 参照)。
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

        static readonly TimeSpan _fastInterval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _slowInterval = TimeSpan.FromMilliseconds(2500);
        static readonly TimeSpan _fastPeriod = TimeSpan.FromSeconds(10);

        readonly List<AIChatMessage> _messages = new();
        CancellationTokenSource? _cts;
        int _version;

        [ScriptHide]
        public IReadOnlyList<AIChatMessage> Messages => _messages;

        /// <summary>表示内容が変わるたびに増える。コンポーネントが自動スクロールの判定に使う。</summary>
        [ScriptHide]
        public int Version => _version;

        /// <summary>会話の識別子。<see cref="Clear"/> で振り直す。</summary>
        public string ConversationId { get; private set; } = Guid.NewGuid().ToString("N");

        /// <summary>返事を待っている間 true。</summary>
        public bool IsBusy => _cts != null;

        /// <summary>最後に確定した返事 (HTML)。まだ無ければ空。</summary>
        public string LastReply { get; private set; } = string.Empty;

        /// <summary>設定済みで送信できるか (エンドポイントかホストフックのどちらかがある)。</summary>
        [ScriptHide]
        public bool IsConfigured => SendCoreAsync != null || (Http != null && !string.IsNullOrEmpty(EndPoint));

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

        /// <summary>発言を送る。待ち中や空文字は無視。返事はポーリングで受け取り <see cref="LastReply"/> と OnReplyReceived に届く。</summary>
        [ScriptName("Send")]
        public async Task SendAsync(string text)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(text) || !IsConfigured) return;

            _messages.Add(new AIChatMessage { IsUser = true, Text = text });
            var assistant = new AIChatMessage { IsRunning = true, SourceText = text };
            _messages.Add(assistant);
            Touch();

            _cts = new CancellationTokenSource();
            try
            {
                await RunAsync(assistant, text, _cts.Token);
            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                assistant.IsRunning = false;
                Touch();
            }

            if (!assistant.IsError)
            {
                LastReply = assistant.Html;
                await Module.ExecuteScriptAsync(Design.OnReplyReceived, assistant.Html);
            }
        }

        /// <summary>待ち中の返事を中断する。サーバーにも中断を伝える。</summary>
        [ScriptName("Cancel")]
        public async Task CancelAsync()
        {
            var cts = _cts;
            if (cts == null) return;
            var running = _messages.LastOrDefault(e => e.IsRunning);
            cts.Cancel();
            if (running != null && !string.IsNullOrEmpty(running.RequestId) && Http != null && SendCoreAsync == null)
            {
                using var _ = Http.AddChecker((_, _) => { });
                await Http.DeleteAsync($"{EndPoint}/{running.RequestId}", loading: false);
            }
        }

        /// <summary>履歴を消して新しい会話にする (待ち中なら中断する)。</summary>
        [ScriptName("Clear")]
        public async Task ClearAsync()
        {
            await CancelAsync();
            _messages.Clear();
            LastReply = string.Empty;
            ConversationId = Guid.NewGuid().ToString("N");
            Touch();
        }

        /// <summary>失敗した発言を同じ内容でもう一度送る (失敗した 2 件を取り除いてから送る)。</summary>
        [ScriptHide]
        public async Task RetryAsync(AIChatMessage failed)
        {
            if (IsBusy || !failed.IsError) return;
            var index = _messages.IndexOf(failed);
            if (index < 0) return;
            _messages.RemoveAt(index);
            if (index > 0 && _messages[index - 1].IsUser) _messages.RemoveAt(index - 1);
            await SendAsync(failed.SourceText);
        }

        async Task RunAsync(AIChatMessage assistant, string text, CancellationToken token)
        {
            var request = new AIChatSendRequest { ConversationId = ConversationId, Message = text };
            try
            {
                if (SendCoreAsync != null)
                {
                    var progress = new Progress<AIChatStatusResponse>(s => { Apply(assistant, s); Touch(); });
                    Apply(assistant, await SendCoreAsync(this, request, progress, token));
                    return;
                }
                await PollAsync(assistant, request, token);
            }
            catch (OperationCanceledException)
            {
                assistant.Error = Properties.Resources.AIChat_Canceled;
            }
            catch (Exception e)
            {
                assistant.Error = e.Message;
            }
        }

        async Task PollAsync(AIChatMessage assistant, AIChatSendRequest request, CancellationToken token)
        {
            var http = Http!;
            //通信エラーはトーストやログに流さず、この発言のエラー表示にまとめる
            string? lastError = null;
            using var checker = http.AddChecker((_, message) => lastError = message);

            var sent = await http.PostAsJsonAsync<AIChatSendRequest, AIChatSendResponse>(EndPoint, request, loading: false);
            if (sent == null || string.IsNullOrEmpty(sent.RequestId))
            {
                assistant.Error = lastError ?? Properties.Resources.AIChat_Failed;
                return;
            }
            assistant.RequestId = sent.RequestId;

            var started = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, Design.TimeoutSeconds));
            while (true)
            {
                var elapsed = DateTime.Now - started;
                await Task.Delay(elapsed < _fastPeriod ? _fastInterval : _slowInterval, token);

                var status = await http.GetFromJsonAsync<AIChatStatusResponse>($"{EndPoint}/{sent.RequestId}", loading: false);
                if (status == null)
                {
                    assistant.Error = token.IsCancellationRequested
                        ? Properties.Resources.AIChat_Canceled
                        : lastError ?? Properties.Resources.AIChat_Failed;
                    return;
                }
                Apply(assistant, status);
                Touch();
                if (!status.IsRunning) return;

                if (DateTime.Now - started > timeout)
                {
                    assistant.Error = Properties.Resources.AIChat_Timeout;
                    return;
                }
            }
        }

        static void Apply(AIChatMessage assistant, AIChatStatusResponse status)
        {
            assistant.Progress = status.Progress;
            if (!string.IsNullOrEmpty(status.Reply)) assistant.Html = status.Reply;
            switch (status.Status)
            {
                case AIChatJobStatus.Error:
                    assistant.Error = string.IsNullOrEmpty(status.Error) ? Properties.Resources.AIChat_Failed : status.Error;
                    break;
                case AIChatJobStatus.Canceled:
                    assistant.Error = Properties.Resources.AIChat_Canceled;
                    break;
            }
        }

        void Touch()
        {
            _version++;
            NotifyStateChanged();
        }
    }
}
