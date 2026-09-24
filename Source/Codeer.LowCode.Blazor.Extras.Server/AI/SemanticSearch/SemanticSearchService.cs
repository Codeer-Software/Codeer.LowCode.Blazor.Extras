using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField (意味検索) のサーバー側の入口。ホストはこれを 1 つ作って (アプリの静的な持ち物か DI のシングルトン) 次の 3 か所から使う。
    /// <list type="bullet">
    /// <item>保存時の索引付け: <see cref="ApplyAsync"/> を <c>ModuleDataIO</c> の派生 (テンプレートの <c>CustomizedModuleDataIO.AddAsync / UpdateAsync</c>) から呼ぶ。
    /// 送られてきた文章に埋め込みベクトルを付けて、書き込み専用列に保存される形にする</item>
    /// <item>再索引 API: <see cref="StartReindexAsync"/> / <see cref="GetReindexStatus"/> / <see cref="CancelReindex"/> を薄い Controller (POST / GET / DELETE) から呼ぶ。
    /// フィールドのスクリプト Reindex がポーリングする (AIChatService と同じ形)</item>
    /// <item>AI チャットの意味検索: <c>RawDataAccessAgent</c> に渡すと search_records と <c>{embed:…}</c> が使えるようになる</item>
    /// </list>
    /// 埋め込みは <see cref="IEmbeddingProvider"/> (Azure OpenAI / 独自。アプリの対応表で選ぶ)。未設定 (null) なら文章だけ保存し、ベクトルは null のまま (検索対象にならない・意味検索ツールも付かない)。
    /// 保存時に埋め込みの呼び出しに失敗しても保存は止めず、ベクトル null で保存して警告ログを出す (再索引で後から埋められる)。
    /// ジョブはプロセス内メモリ。単一インスタンス前提。
    /// <code>
    /// //アプリの静的な持ち物として 1 つ作る
    /// public static SemanticSearchService Service { get; } = new(() => embeddingProvider, () => DesignerService.GetDesignData());
    /// //CustomizedModuleDataIO
    /// protected override async Task&lt;string&gt; AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
    /// {
    ///     await SemanticSearchIndex.Service.ApplyAsync(data, isNewData: true);
    ///     return await base.AddAsync(transactionId, moduleSubmitId, data);
    /// }
    /// </code>
    /// </summary>
    public sealed class SemanticSearchService : IDisposable
    {
        readonly Func<IEmbeddingProvider?> _embeddingProvider;
        readonly Func<DesignData> _design;
        readonly ILogger? _logger;
        readonly ConcurrentDictionary<string, Job> _jobs = new();

        /// <param name="embeddingProvider">埋め込みプロバイダの取り方 (null を返すなら埋め込みなし = 文章だけ保存・意味検索なし)。都度呼ぶので、設定の読み込み失敗を起動時に確定させずに済む</param>
        /// <param name="design">デザインの取り方 (ホットリロードで変わるので都度呼ぶ)</param>
        /// <param name="loggerFactory">埋め込みの失敗を記録する先 (null ならログなし)</param>
        public SemanticSearchService(Func<IEmbeddingProvider?> embeddingProvider, Func<DesignData> design, ILoggerFactory? loggerFactory = null)
        {
            _embeddingProvider = embeddingProvider;
            _design = design;
            _logger = loggerFactory?.CreateLogger<SemanticSearchService>();
        }

        /// <summary>終了した再索引ジョブを結果を取りに来るまで残す時間。</summary>
        public TimeSpan FinishedRetention { get; init; } = TimeSpan.FromMinutes(30);
        /// <summary>これを超えて走り続ける再索引ジョブは中断する。</summary>
        public TimeSpan MaxRunning { get; init; } = TimeSpan.FromHours(6);
        /// <summary>再索引で 1 ページに読んで、まとめて埋め込む行数。</summary>
        public int ReindexPageSize { get; init; } = 100;

        /// <summary>
        /// 保存前に、モジュールの各 SemanticSearchField の文章に埋め込みベクトルを付ける。
        /// 文章はデータに含まれていればそれを使い (クライアントが組み立てたもの・再索引)、無ければ新規行のときだけデータから組み立てる (一括取込など)。
        /// 更新で文章が送られていないとき (対象フィールドが変わっていない) は何もしない。
        /// 文章と一緒にベクトルも送られていれば (再索引がまとめて埋め込んだもの) そのまま使い、埋め込みは呼ばない。
        /// </summary>
        public async Task ApplyAsync(ModuleData data, bool isNewData, CancellationToken cancellationToken = default)
        {
            var designData = _design();
            var module = designData.Modules.Find(data.Name);
            if (module == null) return;
            foreach (var field in module.Fields.OfType<SemanticSearchFieldDesign>())
            {
                if (!field.HasColumns) continue;
                string? text = null;
                if (data.Fields.TryGetValue(field.Name, out var fieldData) && fieldData is SemanticSearchFieldData sent && sent.Text != null)
                {
                    if (sent.Vector != null) continue;
                    text = sent.Text;
                }
                else if (isNewData)
                    text = SemanticSearchText.Build(designData, module, data, field);
                else
                    continue;

                var vector = await EmbedAsync(text, cancellationToken);
                data.Fields[field.Name] = new SemanticSearchFieldData { Text = text, Vector = vector == null ? null : SemanticSearchVector.Encode(vector) };
            }
        }

        /// <summary>
        /// 再索引のワイヤリクエスト (POST {EndPoint}) を受けて起動し requestId を返す。Controller を薄く保つための入口。
        /// ModuleName / FieldName の SemanticSearchField がデザインにあり、そのフィールドを今のユーザーが読めるときだけ受け付ける
        /// (ユーザー権限だけ: アプリアクセス条件・モジュールの UserReadCondition・ユーザーで偽と確定する PermissionField 条件。通らなければ LowCodeException で、ジョブは作られない)。
        /// 行の読み書きは openScope が返す実行ユーザーの ModuleDataIO で行う (読める行だけ・書ける行だけ = 通常の権限)。
        /// 同じモジュールの再索引が走っている間は新しく起こさず、その requestId を返す。
        /// ownerKey は取得・中断時の照合に使う (ログイン ID 等。匿名なら空)。
        /// </summary>
        /// <param name="authorizeWith">入口検査に使う、今のリクエストの ModuleDataIO</param>
        /// <param name="openScope">ジョブが使う ModuleDataIO と DB 接続を新しく開く (リクエストとは別の寿命。ジョブの終了時に Dispose される)</param>
        public async Task<string> StartReindexAsync(string ownerKey, SemanticSearchReindexRequest request, ModuleDataIO authorizeWith, Func<SemanticSearchReindexScope> openScope)
        {
            await FieldApiAuthorization.CheckAsync<SemanticSearchFieldDesign>(authorizeWith, request.ModuleName, request.FieldName, Resources.SemanticSearchField_NotFound);
            return StartReindex(ownerKey, request.ModuleName, request.MissingOnly, openScope);
        }

        /// <summary>再索引の状態を返す。見つからない・所有者が違うときは null (呼び出し側は 404)。</summary>
        public SemanticSearchReindexStatusResponse? GetReindexStatus(string ownerKey, string requestId)
        {
            var job = Find(ownerKey, requestId);
            if (job == null) return null;
            job.LastAccess = DateTime.UtcNow;
            return job.Snapshot();
        }

        /// <summary>再索引を中断する。見つからない・所有者が違うときは false。</summary>
        public bool CancelReindex(string ownerKey, string requestId)
        {
            var job = Find(ownerKey, requestId);
            if (job == null) return false;
            job.Cancel();
            return true;
        }

        public void Dispose()
        {
            foreach (var job in _jobs.Values) job.Cancel();
            _jobs.Clear();
        }

        /// <summary>埋め込みプロバイダ (未設定・作成失敗なら null。失敗は警告ログ)。意味検索ツールが使う。</summary>
        internal IEmbeddingProvider? GetProvider()
        {
            try { return _embeddingProvider(); }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: the embedding provider could not be created.");
                return null;
            }
        }

        /// <summary>再索引を起動し requestId を返す (検査なし。<see cref="StartReindexAsync"/> の後段)。同じモジュールのジョブが走っていればその requestId。</summary>
        internal string StartReindex(string ownerKey, string moduleName, bool missingOnly, Func<SemanticSearchReindexScope> openScope)
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

        /// <summary>保持中の再索引ジョブ数 (テスト用)。</summary>
        internal int ReindexJobCount => _jobs.Count;

        /// <summary>
        /// モジュールの全行の索引を作り直す (フィールドを後から置いたとき・埋め込みモデルを変えたとき・埋め込みに失敗した行を埋めるとき)。
        /// 行は moduleDataIO で読み (実行ユーザーの読み取り権限の範囲)、ページ単位で文章を組み立て、ページ分をまとめて 1 回で埋め込み、
        /// 文章とベクトルを付けて通常の Submit で書く (書き込み権限も通常どおり効く。<see cref="ApplyAsync"/> はベクトル付きなので埋め込みを呼ばない)。
        /// 埋め込みモデルが無いときは文章だけ書き直す。埋め込みの失敗はそのまま例外にする (保存時と違い黙って null にはしない)。
        /// missingOnly はベクトルがまだ無い行だけ (書き込み専用列を db で直接読んで判定する)。progress には (書いた行数, 対象行数) を報告する。戻り値は書いた行数。
        /// </summary>
        internal async Task<int> ReindexAsync(ModuleDataIO moduleDataIO, IDbAccessor db, string moduleName, bool missingOnly = false,
            IProgress<(int Processed, int Total)>? progress = null, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            var designData = _design();
            var module = designData.Modules.Find(moduleName) ?? throw new ArgumentException($"Module '{moduleName}' does not exist.", nameof(moduleName));
            var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).ToList();
            if (fields.Count == 0) throw new ArgumentException($"Module '{moduleName}' has no SemanticSearchField with the text, vector and vector-search columns set.", nameof(moduleName));
            var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault() ?? throw new ArgumentException($"Module '{moduleName}' has no IdField.", nameof(moduleName));
            var rowsPerPage = pageSize ?? ReindexPageSize;

            //missingOnly: どのフィールドかでベクトルが無い行 = 対象。索引済み (全フィールドにベクトルあり) の Id を先に集めて飛ばす
            HashSet<string>? indexed = null;
            if (missingOnly)
            {
                foreach (var field in fields)
                {
                    var ids = await SemanticSearchIndexReader.ReadIndexedIdsAsync(db, module, field, cancellationToken);
                    indexed = indexed == null ? ids : indexed.Intersect(ids).ToHashSet();
                }
            }

            //再索引ではプロバイダ作成の失敗も例外にする (保存時と違い黙って文章だけにはしない)
            var provider = _embeddingProvider();
            var count = 0;
            var total = 0;
            for (var pageIndex = 0; ; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var condition = new SearchCondition(moduleName) { LimitCount = rowsPerPage };
                condition.SortConditions.Add(new SortCondition { Variable = $"{idField.Name}.Value" });
                var page = await moduleDataIO.GetListAsync(condition, pageIndex);
                if (pageIndex == 0)
                {
                    total = indexed == null ? page.TotalCount : Math.Max(0, page.TotalCount - indexed.Count);
                    progress?.Report((0, total));
                }

                var rows = page.Items.Where(row => row.Fields.TryGetValue(idField.Name, out var id) && id is IdFieldData idData && !string.IsNullOrEmpty(idData.Value)
                    && (indexed == null || !indexed.Contains(idData.Value!))).ToList();
                if (rows.Count > 0)
                {
                    //フィールドごとに、ページ分の文章をまとめて 1 回で埋め込む
                    var submits = rows.ToDictionary(row => row, row =>
                    {
                        var update = new ModuleData { Name = moduleName };
                        update.Fields[idField.Name] = row.Fields[idField.Name];
                        return update;
                    });
                    foreach (var field in fields)
                    {
                        var texts = rows.Select(row => SemanticSearchText.Build(designData, module, row, field)).ToList();
                        var vectors = await EmbedAllAsync(provider, texts, cancellationToken);
                        for (var i = 0; i < rows.Count; i++)
                            submits[rows[i]].Fields[field.Name] = new SemanticSearchFieldData { Text = texts[i], Vector = vectors[i] == null ? null : SemanticSearchVector.Encode(vectors[i]!) };
                    }
                    var results = await moduleDataIO.SubmitWithTransactionAsync(rows.Select(row => new ModuleSubmitData
                    {
                        ModuleName = moduleName,
                        Id = ((IdFieldData)row.Fields[idField.Name]!).Value ?? string.Empty,
                        Update = { submits[row] },
                    }).ToList());
                    var error = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.ExceptionMessage))?.ExceptionMessage;
                    if (error != null) throw new InvalidOperationException(error);
                    count += rows.Count;
                    progress?.Report((count, total));
                }
                if (pageIndex + 1 >= page.PageCount || page.Items.Count == 0) break;
            }
            return count;
        }

        /// <summary>文章の埋め込み (保存時)。プロバイダ未設定・空文・失敗は null。</summary>
        internal async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var provider = GetProvider();
            if (provider == null) return null;
            try
            {
                var vectors = await provider.EmbedAsync(new[] { text }, cancellationToken);
                var vector = vectors.Count == 0 ? null : vectors[0];
                CheckDimensions(provider, vector);
                return vector;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: embedding failed. The text is saved without a vector (run a reindex later).");
                return null;
            }
        }

        //まとめて埋め込む (再索引用。失敗は例外)。空文は埋め込まず null
        static async Task<float[]?[]> EmbedAllAsync(IEmbeddingProvider? provider, List<string> texts, CancellationToken cancellationToken)
        {
            var result = new float[]?[texts.Count];
            if (provider == null) return result;
            var targets = Enumerable.Range(0, texts.Count).Where(i => !string.IsNullOrWhiteSpace(texts[i])).ToList();
            if (targets.Count == 0) return result;
            var vectors = await provider.EmbedAsync(targets.Select(i => texts[i]).ToList(), cancellationToken);
            if (vectors.Count != targets.Count) throw new InvalidOperationException($"SemanticSearch: the embedding provider returned {vectors.Count} vectors for {targets.Count} texts.");
            for (var i = 0; i < targets.Count; i++)
            {
                CheckDimensions(provider, vectors[i]);
                result[targets[i]] = vectors[i];
            }
            return result;
        }

        //プロバイダが次元数を申告していれば、返ったベクトルと合っていることを確かめる (DB の列定義との食い違いを早く見つける)
        static void CheckDimensions(IEmbeddingProvider provider, float[]? vector)
        {
            if (vector != null && provider.Dimensions > 0 && vector.Length != provider.Dimensions)
                throw new InvalidOperationException($"SemanticSearch: the embedding provider '{provider.ModelId}' returned {vector.Length} dimensions but declares {provider.Dimensions}.");
        }

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
                var count = await ReindexAsync(scope.ModuleDataIO, scope.DbAccessor, job.ModuleName, missingOnly, job, ReindexPageSize, job.Cancellation.Token);
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
                    if (now - job.LastAccess > FinishedRetention) _jobs.TryRemove(job.Id, out _);
                }
                else if (now - job.Created > MaxRunning)
                {
                    job.Cancel();
                }
            }
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
