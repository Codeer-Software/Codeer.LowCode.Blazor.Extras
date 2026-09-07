using Codeer.LowCode.Blazor.Extras.AIChat;
using System.Collections.Concurrent;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    public class AIChatJobStoreOptions
    {
        /// <summary>終了したジョブを結果を取りに来るまで残す時間。</summary>
        public TimeSpan FinishedRetention { get; set; } = TimeSpan.FromMinutes(30);
        /// <summary>これを超えて走り続けるジョブは中断する (Agent の暴走止め)。</summary>
        public TimeSpan MaxRunning { get; set; } = TimeSpan.FromHours(1);
    }

    /// <summary>
    /// AIChatField のジョブ置き場 (プロセス内メモリ)。送信で Agent をバックグラウンド実行し、
    /// クライアントは requestId で状態をポーリングする。返事は <see cref="ChatReplyHtml"/> で HTML に揃える。
    /// 単一インスタンス前提。スケールアウトするなら ARR アフィニティか共有ストアに置き換える。
    /// シングルトンで登録する (builder.Services.AddSingleton&lt;AIChatJobStore&gt;())。
    /// </summary>
    public class AIChatJobStore : IDisposable
    {
        readonly IAIChatAgent _agent;
        readonly AIChatJobStoreOptions _options;
        readonly ConcurrentDictionary<string, Job> _jobs = new();

        public AIChatJobStore(IAIChatAgent agent) : this(agent, new AIChatJobStoreOptions()) { }

        public AIChatJobStore(IAIChatAgent agent, AIChatJobStoreOptions options)
        {
            _agent = agent;
            _options = options;
        }

        /// <summary>Agent を起動し requestId を返す。ownerKey は取得・中断時の照合に使う (ログイン名等。匿名なら空)。</summary>
        public string Start(string ownerKey, string conversationId, string message)
        {
            Cleanup();
            var job = new Job(ownerKey ?? string.Empty);
            _jobs[job.Id] = job;
            job.Task = Task.Run(() => RunAsync(job, new AIChatAgentRequest
            {
                ConversationId = conversationId ?? string.Empty,
                Message = message ?? string.Empty,
                UserName = ownerKey ?? string.Empty,
            }));
            return job.Id;
        }

        /// <summary>状態を返す。見つからない・所有者が違うときは null (呼び出し側は 404)。</summary>
        public AIChatStatusResponse? GetStatus(string ownerKey, string requestId)
        {
            var job = Find(ownerKey, requestId);
            if (job == null) return null;
            job.LastAccess = DateTime.UtcNow;
            return job.Snapshot();
        }

        /// <summary>中断する。見つからない・所有者が違うときは false。</summary>
        public bool Cancel(string ownerKey, string requestId)
        {
            var job = Find(ownerKey, requestId);
            if (job == null) return false;
            job.Cancel();
            return true;
        }

        /// <summary>保持中のジョブ数 (テスト・監視用)。</summary>
        public int Count => _jobs.Count;

        Job? Find(string ownerKey, string requestId)
        {
            if (string.IsNullOrEmpty(requestId) || !_jobs.TryGetValue(requestId, out var job)) return null;
            return job.OwnerKey == (ownerKey ?? string.Empty) ? job : null;
        }

        async Task RunAsync(Job job, AIChatAgentRequest request)
        {
            try
            {
                var reply = await _agent.ReplyAsync(request, job, job.Cancellation.Token);
                job.Complete(ChatReplyHtml.Normalize(reply));
            }
            catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
            {
                job.SetCanceled();
            }
            catch (Exception e)
            {
                job.Fail(e.Message);
            }
        }

        void Cleanup()
        {
            var now = DateTime.UtcNow;
            foreach (var job in _jobs.Values)
            {
                if (job.IsFinished)
                {
                    if (now - job.LastAccess > _options.FinishedRetention) _jobs.TryRemove(job.Id, out _);
                }
                else if (now - job.Created > _options.MaxRunning)
                {
                    job.Cancel();
                }
            }
        }

        public void Dispose()
        {
            foreach (var job in _jobs.Values) job.Cancel();
            _jobs.Clear();
        }

        sealed class Job : IAIChatProgress
        {
            readonly object _lock = new();
            string _status = AIChatJobStatus.Running;
            string _reply = string.Empty;
            string _progress = string.Empty;
            string _error = string.Empty;

            public Job(string ownerKey) => OwnerKey = ownerKey;

            public string Id { get; } = Guid.NewGuid().ToString("N");
            public string OwnerKey { get; }
            public DateTime Created { get; } = DateTime.UtcNow;
            public DateTime LastAccess { get; set; } = DateTime.UtcNow;
            public CancellationTokenSource Cancellation { get; } = new();
            public Task? Task { get; set; }
            public bool IsFinished => _status != AIChatJobStatus.Running;

            public void Report(string progressText)
            {
                lock (_lock) { if (!IsFinished) _progress = progressText ?? string.Empty; }
            }

            public void ReportPartial(AIChatReply partialReply)
            {
                var html = ChatReplyHtml.Normalize(partialReply);
                lock (_lock) { if (!IsFinished) _reply = html; }
            }

            public void Complete(string html)
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _reply = html;
                    _progress = string.Empty;
                    _status = AIChatJobStatus.Done;
                }
            }

            public void Fail(string error)
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _error = error;
                    _progress = string.Empty;
                    _status = AIChatJobStatus.Error;
                }
            }

            public void SetCanceled()
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _progress = string.Empty;
                    _status = AIChatJobStatus.Canceled;
                }
            }

            //Agent がトークンを無視して走り続けても、クライアントには即 canceled を見せる (以後の Complete/Fail は無視される)
            public void Cancel()
            {
                SetCanceled();
                try { Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            }

            public AIChatStatusResponse Snapshot()
            {
                lock (_lock)
                {
                    return new AIChatStatusResponse { Status = _status, Reply = _reply, Progress = _progress, Error = _error };
                }
            }
        }
    }
}
