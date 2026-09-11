using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Services;
using System.Net;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 1 つのチャットの会話 (発言の列・会話 ID・待ち中のジョブ)。<see cref="AIChatField"/> はこれの表示と入力を担う薄い層で、
    /// 会話そのものは <see cref="AIChatConversationStore"/> (アプリの寿命 = WASM ならタブ) に置いて画面遷移をまたいで生き残る。
    /// 返事待ちのポーリングもここで続けるので、返事の中のリンクで別ページへ行っている間に返事が届き、戻った瞬間に表示される。
    /// </summary>
    internal sealed class AIChatConversation
    {
        static readonly TimeSpan _fastInterval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _slowInterval = TimeSpan.FromMilliseconds(2500);
        static readonly TimeSpan _fastPeriod = TimeSpan.FromSeconds(10);

        //サーバーへ送る会話の写しの上限 (サーバー履歴が消えていたときの文脈復元用。トークンの歯止め)
        const int TranscriptMaxTurns = 6;
        const int TranscriptMaxChars = 4000;

        readonly List<AIChatMessage> _messages = new();
        CancellationTokenSource? _cts;

        public IReadOnlyList<AIChatMessage> Messages => _messages;
        public string ConversationId { get; private set; } = Guid.NewGuid().ToString("N");
        public string LastReply { get; private set; } = string.Empty;
        public bool IsBusy => _cts != null;
        /// <summary>表示内容が変わるたびに増える。</summary>
        public int Version { get; private set; }

        /// <summary>今この会話を表示しているフィールドへの通知 (無ければ画面から離れている)。</summary>
        public Action? Changed { get; set; }
        /// <summary>返事が確定したときの通知 (OnReplyReceived スクリプト)。画面から離れていれば呼ばれない。</summary>
        public Func<string, Task>? ReplyReceived { get; set; }

        /// <summary>送信に必要なもの (フィールドが渡す。デザインの値と HTTP)。</summary>
        public sealed class SendContext
        {
            public required AIChatField Field { get; init; }
            public IHttpService? Http { get; init; }
            public string EndPoint { get; init; } = string.Empty;
            public string Agent { get; init; } = string.Empty;
            public string DocumentFolder { get; init; } = string.Empty;
            public int TimeoutSeconds { get; init; } = 600;
        }

        public async Task SendAsync(string text, SendContext context)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(text)) return;
            var transcript = BuildTranscript();
            _messages.Add(new AIChatMessage { IsUser = true, Text = text });
            var assistant = new AIChatMessage { IsRunning = true, SourceText = text };
            _messages.Add(assistant);
            Touch();

            _cts = new CancellationTokenSource();
            try
            {
                await RequestAsync(assistant, text, transcript, context, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                assistant.Error = Properties.Resources.AIChat_Canceled;
            }
            catch (Exception e)
            {
                assistant.Error = e.Message;
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
                var handler = ReplyReceived;
                if (handler != null) await handler(assistant.Html);
            }
        }

        public async Task CancelAsync(SendContext? context)
        {
            var cts = _cts;
            if (cts == null) return;
            var running = _messages.LastOrDefault(e => e.IsRunning);
            cts.Cancel();
            if (running != null && !string.IsNullOrEmpty(running.RequestId) && context?.Http != null && AIChatField.SendCoreAsync == null)
            {
                using var _ = context.Http.AddChecker((_, _) => { });
                await context.Http.DeleteAsync($"{context.EndPoint}/{running.RequestId}", loading: false);
            }
        }

        public async Task ClearAsync(SendContext? context)
        {
            await CancelAsync(context);
            _messages.Clear();
            LastReply = string.Empty;
            ConversationId = Guid.NewGuid().ToString("N");
            Touch();
        }

        public async Task RetryAsync(AIChatMessage failed, SendContext context)
        {
            if (IsBusy || !failed.IsError) return;
            var index = _messages.IndexOf(failed);
            if (index < 0) return;
            _messages.RemoveAt(index);
            if (index > 0 && _messages[index - 1].IsUser) _messages.RemoveAt(index - 1);
            Touch();
            await SendAsync(failed.SourceText, context);
        }

        async Task RequestAsync(AIChatMessage assistant, string text, List<AIChatTranscriptMessage> transcript, SendContext context, CancellationToken token)
        {
            var request = new AIChatSendRequest
            {
                ConversationId = ConversationId, Message = text, Agent = context.Agent, DocumentFolder = context.DocumentFolder, Transcript = transcript,
                ModuleName = context.Field.Module?.Design.Name ?? string.Empty, FieldName = context.Field.Design.Name,
            };
            if (AIChatField.SendCoreAsync != null)
            {
                var progress = new Progress<AIChatStatusResponse>(s => { Apply(assistant, s); Touch(); });
                Apply(assistant, await AIChatField.SendCoreAsync(context.Field, request, progress, token));
                return;
            }

            var http = context.Http ?? throw new InvalidOperationException("IHttpService is not available.");
            //通信エラーはトーストやログに流さず、この発言のエラー表示にまとめる
            string? lastError = null;
            using var checker = http.AddChecker((_, message) => lastError = message);
            var sent = await http.PostAsJsonAsync<AIChatSendRequest, AIChatSendResponse>(context.EndPoint, request, loading: false);
            if (sent == null || string.IsNullOrEmpty(sent.RequestId))
            {
                assistant.Error = lastError ?? Properties.Resources.AIChat_Failed;
                return;
            }
            assistant.RequestId = sent.RequestId;

            var started = DateTime.Now;
            var timeout = TimeSpan.FromSeconds(Math.Max(1, context.TimeoutSeconds));
            while (true)
            {
                var elapsed = DateTime.Now - started;
                await Task.Delay(elapsed < _fastPeriod ? _fastInterval : _slowInterval, token);

                var status = await http.GetFromJsonAsync<AIChatStatusResponse>($"{context.EndPoint}/{sent.RequestId}", loading: false);
                if (status == null)
                {
                    assistant.Error = token.IsCancellationRequested ? Properties.Resources.AIChat_Canceled
                        : lastError != null && !lastError.Contains("404") ? lastError
                        : Properties.Resources.AIChat_ReplyLost;
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

        //これまでの会話の写し (テキストだけ・直近 TranscriptMaxTurns 往復・TranscriptMaxChars 文字まで)。失敗や待ち中の発言は含めない
        List<AIChatTranscriptMessage> BuildTranscript()
        {
            var result = new List<AIChatTranscriptMessage>();
            var turns = 0; var chars = 0;
            for (var i = _messages.Count - 1; i >= 0 && turns < TranscriptMaxTurns; i--)
            {
                var m = _messages[i];
                if (m.IsRunning || m.IsError) continue;
                var text = m.IsUser ? m.Text : HtmlToText(m.Html);
                if (string.IsNullOrWhiteSpace(text)) continue;
                if (text.Length > TranscriptMaxChars) text = text[..TranscriptMaxChars] + "…";
                if (chars + text.Length > TranscriptMaxChars && result.Count > 0) break;
                chars += text.Length;
                result.Add(new AIChatTranscriptMessage { IsUser = m.IsUser, Text = text });
                if (m.IsUser) turns++;
            }
            result.Reverse();
            return result;
        }

        static readonly Regex _tags = new("<[^>]+>", RegexOptions.Compiled);
        static readonly Regex _spaces = new(@"[ \t]+", RegexOptions.Compiled);

        internal static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            //SVG グラフはテキストにしても意味が無いので丸ごと落とす
            var s = Regex.Replace(html, "<svg.*?</svg>", "[グラフ]", RegexOptions.Singleline);
            s = Regex.Replace(s, @"</(p|div|tr|li|h[1-6]|table|br)>|<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"</t[dh]>", "\t", RegexOptions.IgnoreCase);
            s = _tags.Replace(s, string.Empty);
            s = WebUtility.HtmlDecode(s);
            s = _spaces.Replace(s, " ");
            return Regex.Replace(s, @"\n{3,}", "\n\n").Trim();
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
            Version++;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 会話の保管庫。DI のスコープ (フィールドが持つ <see cref="Blazor.RequestInterfaces.Services"/>。WASM ならタブ、Blazor Server なら回線 = ユーザー、
    /// デスクトップならアプリ) ごとに 1 つ。ホストに DI 登録を求めずにスコープと同じ寿命にするため、Services インスタンスに弱参照で紐づける
    /// (スコープが消えれば会話も GC される。型に紐づく共有状態にはしない)。
    /// 鍵は「モジュール名 + フィールド名」で、同じ画面に戻ると同じ会話が戻る。リロードで消える (意図的なやり直し)。
    /// </summary>
    internal sealed class AIChatConversationStore
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Blazor.RequestInterfaces.Services, AIChatConversationStore> _perScope = new();

        readonly Dictionary<string, AIChatConversation> _conversations = new();

        /// <summary>そのスコープの保管庫。</summary>
        public static AIChatConversationStore For(Blazor.RequestInterfaces.Services services) => _perScope.GetValue(services, _ => new AIChatConversationStore());

        public AIChatConversation GetOrCreate(string key)
        {
            lock (_conversations)
            {
                if (!_conversations.TryGetValue(key, out var conversation)) _conversations[key] = conversation = new AIChatConversation();
                return conversation;
            }
        }
    }
}
