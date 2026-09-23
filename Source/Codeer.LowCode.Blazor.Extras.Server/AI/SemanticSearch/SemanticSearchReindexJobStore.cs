using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using System.Collections.Concurrent;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    public class SemanticSearchReindexJobStoreOptions
    {
        /// <summary>終了したジョブを結果を取りに来るまで残す時間。</summary>
        public TimeSpan FinishedRetention { get; set; } = TimeSpan.FromMinutes(30);
        /// <summary>これを超えて走り続けるジョブは中断する。</summary>
        public TimeSpan MaxRunning { get; set; } = TimeSpan.FromHours(6);
        /// <summary>1 ページで読んで、まとめて埋め込む行数。</summary>
        public int PageSize { get; set; } = 100;
    }

    /// <summary>
    /// SemanticSearchField の再索引ジョブ置き場 (プロセス内メモリ。AIChatJobStore と同じ形)。
    /// 開始 (<see cref="StartAsync"/>) でバックグラウンドの再索引を起動し、クライアント (SemanticSearchField のスクリプト Reindex) は requestId で進捗をポーリングする。
    /// 同じモジュール・フィールドの再索引が走っている間は新しく起こさず、その requestId を返す。
    /// 単一インスタンス前提。1 つだけ作って使い回す (アプリ側の静的プロパティか DI のシングルトン)。
    /// </summary>
    public class SemanticSearchReindexJobStore : IDisposable
    {
        readonly SemanticSearchIndexer _indexer;
        readonly Func<DesignData> _design;
        readonly SemanticSearchReindexJobStoreOptions _options;
        readonly ConcurrentDictionary<string, Job> _jobs = new();

        /// <param name="indexer">埋め込みを付ける Indexer (保存時に使っているものと同じ)</param>
        /// <param name="design">デザインの取り方 (ジョブ実行時に読む)</param>
        public SemanticSearchReindexJobStore(SemanticSearchIndexer indexer, Func<DesignData> design) : this(indexer, design, new SemanticSearchReindexJobStoreOptions()) { }

        public SemanticSearchReindexJobStore(SemanticSearchIndexer indexer, Func<DesignData> design, SemanticSearchReindexJobStoreOptions options)
        {
            _indexer = indexer;
            _design = design;
            _options = options;
        }

        /// <summary>
        /// 開始のワイヤリクエスト (POST {EndPoint}) を受けて再索引を起動し requestId を返す。Controller を薄く保つための入口。
        /// ModuleName / FieldName の SemanticSearchField がデザインにあり、そのフィールドを今のユーザーが読めるときだけ受け付ける
        /// (ユーザー権限だけ: アプリアクセス条件・モジュールの UserReadCondition・ユーザーで偽と確定する PermissionField 条件。通らなければ LowCodeException で、ジョブは作られない)。
        /// 行の読み書きは openScope が返す実行ユーザーの ModuleDataIO で行う (読める行だけ・書ける行だけ = 通常の権限)。
        /// ownerKey は取得・中断時の照合に使う (ログイン ID 等。匿名なら空)。
        /// </summary>
        /// <param name="authorizeWith">入口検査に使う、今のリクエストの ModuleDataIO</param>
        /// <param name="openScope">ジョブが使う ModuleDataIO と DB 接続を新しく開く (リクエストとは別の寿命。ジョブの終了時に Dispose される)</param>
        public async Task<string> StartAsync(string ownerKey, SemanticSearchReindexRequest request, ModuleDataIO authorizeWith, Func<SemanticSearchReindexScope> openScope)
        {
            await FieldApiAuthorization.CheckAsync<SemanticSearchFieldDesign>(authorizeWith, request.ModuleName, request.FieldName, Resources.SemanticSearchField_NotFound);
            return Start(ownerKey, request.ModuleName, request.MissingOnly, openScope);
        }

        /// <summary>再索引を起動し requestId を返す (検査なし。<see cref="StartAsync"/> の後段)。同じモジュールのジョブが走っていればその requestId。</summary>
        internal string Start(string ownerKey, string moduleName, bool missingOnly, Func<SemanticSearchReindexScope> openScope)
        {
            Cleanup();
            var key = moduleName ?? string.Empty;
            var running = _jobs.Values.FirstOrDefault(j => !j.IsFinished && string.Equals(j.ModuleName, key, StringComparison.OrdinalIgnoreCase));
            if (running != null)
            {
                running.Watchers.Add(ownerKey ?? string.Empty);
                return running.Id;
            }

            var job = new Job(ownerKey ?? string.Empty, key);
            _jobs[job.Id] = job;
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            job.Task = Task.Run(() =>
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
                return RunAsync(job, missingOnly, openScope);
            });
            return job.Id;
        }

        /// <summary>状態を返す。見つからない・所有者が違うときは null (呼び出し側は 404)。</summary>
        public SemanticSearchReindexStatusResponse? GetStatus(string ownerKey, string requestId)
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

        /// <summary>保持中のジョブ数 (テスト用)。</summary>
        internal int Count => _jobs.Count;

        Job? Find(string ownerKey, string requestId)
        {
            if (string.IsNullOrEmpty(requestId) || !_jobs.TryGetValue(requestId, out var job)) return null;
            //同じモジュールの走行中ジョブは起動者以外にも requestId を返しているので、所有者に加えて起動者以外の照合も許す
            return job.OwnerKey == (ownerKey ?? string.Empty) || job.Watchers.Contains(ownerKey ?? string.Empty) ? job : null;
        }

        async Task RunAsync(Job job, bool missingOnly, Func<SemanticSearchReindexScope> openScope)
        {
            try
            {
                await using var scope = openScope();
                var count = await _indexer.ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, _design(), job.ModuleName, missingOnly, job, _options.PageSize, job.Cancellation.Token);
                job.Complete(count);
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

        sealed class Job(string ownerKey, string moduleName) : IProgress<(int Processed, int Total)>
        {
            readonly object _lock = new();
            string _status = AIChatJobStatus.Running;
            int _processed;
            int _total;
            string _error = string.Empty;

            public string Id { get; } = Guid.NewGuid().ToString("N");
            public string OwnerKey { get; } = ownerKey;
            public string ModuleName { get; } = moduleName;
            public ConcurrentBag<string> Watchers { get; } = new();
            public DateTime Created { get; } = DateTime.UtcNow;
            public DateTime LastAccess { get; set; } = DateTime.UtcNow;
            public CancellationTokenSource Cancellation { get; } = new();
            public Task? Task { get; set; }
            public bool IsFinished => _status != AIChatJobStatus.Running;

            public void Report((int Processed, int Total) value) => Report(value.Processed, value.Total);

            public void Report(int processed, int total)
            {
                lock (_lock) { if (!IsFinished) { _processed = processed; _total = total; } }
            }

            public void Complete(int processed)
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _processed = processed;
                    if (_total < processed) _total = processed;
                    _status = AIChatJobStatus.Done;
                }
            }

            public void Fail(string error)
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _error = error;
                    _status = AIChatJobStatus.Error;
                }
            }

            public void SetCanceled()
            {
                lock (_lock)
                {
                    if (IsFinished) return;
                    _status = AIChatJobStatus.Canceled;
                }
            }

            //処理がトークンを無視して走り続けても、クライアントには即 canceled を見せる (以後の Complete/Fail は無視される)
            public void Cancel()
            {
                SetCanceled();
                try { Cancellation.Cancel(); } catch (ObjectDisposedException) { }
            }

            public SemanticSearchReindexStatusResponse Snapshot()
            {
                lock (_lock)
                {
                    return new SemanticSearchReindexStatusResponse { Status = _status, Processed = _processed, Total = _total, Error = _error };
                }
            }
        }
    }
}
