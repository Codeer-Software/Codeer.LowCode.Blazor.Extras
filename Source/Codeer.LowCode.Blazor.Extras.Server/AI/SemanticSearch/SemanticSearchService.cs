using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository;
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
    /// <item>保存時の索引付け: <see cref="Interceptor"/> を <c>ModuleDataIO</c> に登録する (テンプレートの <c>CustomizedModuleDataIO</c> のコンストラクタで AddInterceptor)。
    /// 保存のたびに保存した行をサーバーで読み直し (権限を通さない内部読み)、文章と埋め込みベクトルを書き込み専用列だけに書く (レコードの更新ではない)。クライアントは何も送らない。
    /// 一括 INSERT (<c>BulkAddAsync</c>。行を読み戻せない) だけは <see cref="ApplyAsync(IEnumerable{ModuleData}, CancellationToken)"/> で取り込んだ内容から作る</item>
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
    /// //CustomizedModuleDataIO のコンストラクタ
    /// AddInterceptor(SemanticSearchIndex.Service.Interceptor);
    /// //一括 INSERT だけ
    /// protected override async Task BulkAddAsync(Guid transactionId, List&lt;ModuleData&gt; datas)
    /// {
    ///     await SemanticSearchIndex.Service.ApplyAsync(datas);
    ///     await base.BulkAddAsync(transactionId, datas);
    /// }
    /// </code>
    /// </summary>
    public sealed class SemanticSearchService : IDisposable
    {
        readonly Func<IEmbeddingProvider?> _embeddingProvider;
        readonly Func<DesignData> _design;

        /// <summary>今のデザイン (インターセプタが使う)。</summary>
        internal DesignData GetDesign() => _design();
        ILoggerFactory? _loggerFactory;
        ILogger? _logger;

        /// <summary>警告ログの出し先 (埋め込みの失敗・一括 INSERT の結線漏れなど)。ホストが DI を組み立てた後に設定する (静的に作るサービスなので ctor では渡せないことがある)。</summary>
        public ILoggerFactory? LoggerFactory
        {
            get => _loggerFactory;
            set { _loggerFactory = value; _logger = value?.CreateLogger<SemanticSearchService>(); }
        }
        readonly ConcurrentDictionary<string, Job> _jobs = new();

        /// <param name="embeddingProvider">埋め込みプロバイダの取り方 (null を返すなら埋め込みなし = 文章だけ保存・意味検索なし)。都度呼ぶので、設定の読み込み失敗を起動時に確定させずに済む</param>
        /// <param name="design">デザインの取り方 (ホットリロードで変わるので都度呼ぶ)</param>
        /// <param name="loggerFactory">埋め込みの失敗を記録する先 (null ならログなし)</param>
        public SemanticSearchService(Func<IEmbeddingProvider?> embeddingProvider, Func<DesignData> design, ILoggerFactory? loggerFactory = null)
        {
            _embeddingProvider = embeddingProvider;
            _design = design;
            LoggerFactory = loggerFactory;
        }

        /// <summary>終了した再索引ジョブを結果を取りに来るまで残す時間。</summary>
        public TimeSpan FinishedRetention { get; init; } = TimeSpan.FromMinutes(30);
        /// <summary>これを超えて走り続ける再索引ジョブは中断する。</summary>
        public TimeSpan MaxRunning { get; init; } = TimeSpan.FromHours(6);
        /// <summary>再索引で 1 ページに読んで、まとめて埋め込む行数。</summary>
        public int ReindexPageSize { get; init; } = 100;
        /// <summary>保存時の埋め込みの上限時間 (保存のトランザクションの中で待つので短めに)。超えたら文章だけ保存して警告ログ (再索引で埋める)。</summary>
        public TimeSpan SaveEmbeddingTimeout { get; init; } = TimeSpan.FromSeconds(15);

        /// <summary>
        /// 一括 INSERT (<c>ModuleDataIO.BulkAddAsync</c>) 用: 取り込む行の文章を、書く前にその行のデータから作る (<see cref="SemanticSearchFieldData"/> として各行に入れる。ベクトルは付けない)。
        /// 一括 INSERT は採番した Id を返さないので、保存後に行を読み直す <see cref="Interceptor"/> では索引を作れない。
        /// 文章だけなのでキーワード検索は取込直後から効く。ベクトル (と送信データに無いリンクの表示名) は取込後の ReindexMissing / Reindex で付ける
        /// (大量取込の埋め込みを保存のトランザクションの中で待たない)。すでにこのフィールドのデータが入っている行は触らない。通常の保存 (AddAsync / UpdateAsync) では呼ばなくてよい。
        /// 全項目を持つ新規の行にだけ使う (更新の data = 変えた項目だけ、で呼ぶと部分的な文章で上書きされる。内部経路の更新は再索引)。
        /// </summary>
        public Task ApplyAsync(IEnumerable<ModuleData> datas, CancellationToken cancellationToken = default)
        {
            WarnIfInterceptorNotRegistered();
            var designData = _design();
            foreach (var group in datas.GroupBy(d => d.Name))
            {
                var module = designData.Modules.Find(group.Key);
                if (module == null) continue;
                foreach (var field in module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns))
                {
                    foreach (var row in group.Where(d => !d.Fields.ContainsKey(field.Name)))
                        row.Fields[field.Name] = new SemanticSearchFieldData { Text = SemanticSearchText.Build(designData, module, row, field), Vector = null };
                }
            }
            return Task.CompletedTask;
        }

        /// <summary>旧形式。新規行 (isNewData = true) なら <see cref="ApplyAsync(IEnumerable{ModuleData}, CancellationToken)"/> と同じ。更新では何もしない (索引は <see cref="Interceptor"/> が保存後の行から作る)。</summary>
        [Obsolete("Register Interceptor with AddInterceptor (required since 0.21); call ApplyAsync(datas) only from BulkAddAsync.")]
        public Task ApplyAsync(ModuleData data, bool isNewData, CancellationToken cancellationToken = default)
        {
            WarnIfInterceptorNotRegistered();
            return isNewData ? ApplyAsync([data], cancellationToken) : Task.CompletedTask;
        }

        //Interceptor が未登録 (0.17 以前のテンプレートの旧結線のまま、または AddInterceptor を忘れた) だと索引が作り直されない。ApplyAsync が呼ばれたときに 1 回知らせる
        void WarnIfInterceptorNotRegistered()
        {
            if (_interceptor != null || _warnedInterceptorNotRegistered) return;
            _warnedInterceptorNotRegistered = true;
            _logger?.LogWarning("SemanticSearch: Interceptor is not registered. Since 0.21 the index is built by Interceptor; register it with AddInterceptor(SemanticSearchService.Interceptor) in your ModuleDataIO, otherwise saved rows are not indexed.");
        }
        bool _warnedInterceptorNotRegistered;

        /// <summary>
        /// 再索引のワイヤリクエスト (POST {EndPoint}) を受けて起動し requestId を返す。Controller を薄く保つための入口。
        /// ModuleName / FieldName の SemanticSearchField がデザインにあり、そのフィールドに今のユーザーが書けるときだけ受け付ける (索引を書き直す = 書き込み。
        /// ユーザー権限だけ: アプリアクセス条件・モジュールの UserWriteCondition・ユーザーで偽と確定する PermissionField の書き込み条件。通らなければ LowCodeException で、ジョブは作られない)。
        /// 行の読み書きは権限を通さない (保存時の索引付けと同じ。openScope が返す ModuleDataIO の内部操作で全行を読み、書き込み専用列だけを書く)。
        /// 同じモジュールの再索引が走っている間は新しく起こさず、その requestId を返す。
        /// ownerKey は取得・中断時の照合に使う (ログイン ID 等。匿名なら空)。
        /// </summary>
        /// <param name="authorizeWith">入口検査に使う、今のリクエストの ModuleDataIO</param>
        /// <param name="openScope">ジョブが使う ModuleDataIO と DB 接続を新しく開く (リクエストとは別の寿命。ジョブの終了時に Dispose される)</param>
        public async Task<string> StartReindexAsync(string ownerKey, SemanticSearchReindexRequest request, ModuleDataIO authorizeWith, Func<SemanticSearchReindexScope> openScope)
        {
            await FieldApiAuthorization.CheckWriteAsync<SemanticSearchFieldDesign>(authorizeWith, request.ModuleName, request.FieldName, Resources.SemanticSearchField_NotFound);
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

        IModuleDataIOInterceptor? _interceptor;

        /// <summary>
        /// ホストの ModuleDataIO に登録するインターセプタ (<c>AddInterceptor(SemanticSearchIndex.Service.Interceptor)</c>)。
        /// 保存のたびに保存した行から索引 (文章とベクトル) を作って書き、画面の検索欄 (検索レイアウトに置いた SemanticSearchField) の文章には検索の前に埋め込みベクトルを入れる。
        /// </summary>
        public IModuleDataIOInterceptor Interceptor => _interceptor ??= new SemanticSearchInterceptor(this);

        /// <summary><see cref="Interceptor"/> の旧名。</summary>
        [Obsolete("Use Interceptor (it also builds the index when records are saved).")]
        public IModuleDataIOInterceptor ConditionInterceptor => Interceptor;

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

        /// <summary>
        /// 再索引を起動し requestId を返す (検査なし。<see cref="StartReindexAsync"/> の後段)。同じモジュールのジョブが走っていれば、それに含まれるとき (走っているのが全行、または両方が欠損だけ) はその requestId に合流。
        /// 欠損だけが走っている間に全行を起こすのは合流できないので LowCodeException。
        /// </summary>
        internal string StartReindex(string ownerKey, string moduleName, bool missingOnly, Func<SemanticSearchReindexScope> openScope)
        {
            Cleanup();
            var key = moduleName ?? string.Empty;
            Job job;
            //走っているジョブの検査と登録は 1 組で (同時の 2 リクエストが両方起動しないように)
            lock (_jobs)
            {
                var running = _jobs.Values.FirstOrDefault(j => !j.IsFinished && string.Equals(j.ModuleName, key, StringComparison.OrdinalIgnoreCase));
                if (running != null)
                {
                    if (running.MissingOnly && !missingOnly) throw LowCodeException.Create(Resources.SemanticSearch_ReindexMissingRunning, key);
                    running.Watchers.Add(ownerKey ?? string.Empty);
                    return running.Id;
                }
                job = new Job(ownerKey ?? string.Empty, key, missingOnly);
                _jobs[job.Id] = job;
            }
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            //リクエストの実行コンテキスト (監査ログの AuditContext.Current など AsyncLocal) をジョブに引き継がない。
            //ジョブはリクエストの外で動くので、応答済みのリクエストの記録に書き足すことになる
            using (ExecutionContext.SuppressFlow())
            {
                job.Task = Task.Run(() =>
                {
                    CultureInfo.CurrentCulture = culture;
                    CultureInfo.CurrentUICulture = uiCulture;
                    return RunAsync(job, missingOnly, openScope);
                });
            }
            return job.Id;
        }

        /// <summary>保持中の再索引ジョブ数 (テスト用)。</summary>
        internal int ReindexJobCount => _jobs.Count;

        /// <summary>
        /// モジュールの全行の索引を作り直す (フィールドを後から置いたとき・埋め込みモデルを変えたとき・埋め込みに失敗した行を埋めるとき)。
        /// 行は Id だけをページ読みし、1 行ずつ保存時の索引付けと同じ内部読み (<see cref="ReadForIndexAsync"/>。権限を通さない = 誰が起こしても同じ文章になる。従属レコードも権限なし) で読み、
        /// ページ分をまとめて 1 回で埋め込み、保存時と同じく書き込み専用列だけに書く (レコードの更新ではない = 更新日時・楽観ロックの版・ExecuteSql・監査・編集履歴に乗らない)。読んだ後に消えた行は飛ばす。
        /// 埋め込みモデルが無いときは文章だけ書き直す。埋め込みの失敗はそのまま例外にする (保存時と違い黙って null にはしない)。
        /// missingOnly はベクトルがまだ無い行だけ (書き込み専用列を db で直接読んで判定する)。progress には (書いた行数, 対象行数) を報告する。戻り値は書いた行数。
        /// </summary>
        internal async Task<int> ReindexAsync(ModuleDataIO moduleDataIO, IDbAccessor db, string moduleName, bool missingOnly = false,
            IProgress<(int Processed, int Total)>? progress = null, int? pageSize = null, CancellationToken cancellationToken = default)
        {
            var designData = _design();
            var module = designData.Modules.Find(moduleName) ?? throw new ArgumentException($"Module '{moduleName}' does not exist.", nameof(moduleName));
            var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).ToList();
            if (fields.Count == 0) throw new ArgumentException($"Module '{moduleName}' has no SemanticSearchField with the columns set.", nameof(moduleName));
            var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault(e => e.Name == SystemFieldNames.Id) ?? throw new ArgumentException($"Module '{moduleName}' has no IdField.", nameof(moduleName));
            var rowsPerPage = pageSize ?? ReindexPageSize;
            //ページ読みは Id だけ。行の中身は 1 行ずつ保存時と同じ内部読みで取る (ページ読みの同梱は子モジュールの閲覧条件を見るので、タグ付け行が欠けることがある)
            var selectFields = new List<string> { idField.Name };

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
                var condition = new SearchCondition(moduleName) { LimitCount = rowsPerPage, SelectFields = selectFields.ToList() };
                //並びは Id (複合 Id ならその各列。Id 自体は列を持たないので並びに入らない)
                if (idField.CompositeIdVariables.Count > 0) condition.SortConditions.AddRange(idField.CompositeIdVariables.Select(v => new SortCondition { Variable = v }));
                else condition.SortConditions.Add(new SortCondition { Variable = $"{idField.Name}.Value" });
                var page = await moduleDataIO.Internal.GetListAsync(condition, pageIndex);
                if (pageIndex == 0)
                {
                    total = indexed == null ? page.TotalCount : Math.Max(0, page.TotalCount - indexed.Count);
                    progress?.Report((0, total));
                }

                var ids = page.Items.Select(row => row.Fields.TryGetValue(idField.Name, out var id) && id is IdFieldData idData ? idData.Value : null)
                    .Where(id => !string.IsNullOrEmpty(id) && (indexed == null || !indexed.Contains(id!))).Select(id => id!).ToList();
                var rows = new List<ModuleData>();
                foreach (var id in ids)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var row = await ReadForIndexAsync(moduleDataIO.Internal, module, id, fields);
                    if (row != null) rows.Add(row);
                }
                if (rows.Count > 0)
                {
                    //フィールドごとに、ページ分の文章をまとめて 1 回で埋め込む
                    var updates = rows.Select(row =>
                    {
                        var update = new ModuleData { Name = moduleName };
                        update.Fields[idField.Name] = row.Fields[idField.Name];
                        return update;
                    }).ToList();
                    foreach (var field in fields)
                    {
                        var texts = rows.Select(row => SemanticSearchText.Build(designData, module, row, field)).ToList();
                        var vectors = await EmbedAllAsync(provider, texts.Select(t => SemanticSearchText.ForEmbedding(field, t)).ToList(), cancellationToken);
                        for (var i = 0; i < rows.Count; i++)
                            updates[i].Fields[field.Name] = new SemanticSearchFieldData { Text = texts[i], Vector = vectors[i] == null ? null : SemanticSearchVector.Encode(vectors[i]!) };
                    }
                    //書き込み専用列だけを書く (保存時の索引付けと同じ。レコードの更新ではない)。読んだ後に消えた行は飛ばす
                    foreach (var update in updates)
                        if (await moduleDataIO.Internal.UpdateWriteOnlyColumnsAsync(update)) count++;
                    progress?.Report((count, total));
                }
                if (pageIndex + 1 >= page.PageCount || page.Items.Count == 0) break;
            }
            return count;
        }

        /// <summary>
        /// 索引用に行を 1 件読む (保存時の索引付けと再索引で同じ)。権限を通さない内部読み。従属レコードは文章に使うもの (SourceFields にある宣言 = タグ付けの行など) だけ付ける。
        /// 論理削除した行・無い行は null。
        /// </summary>
        internal static async Task<ModuleData?> ReadForIndexAsync(ModuleDataIOInternalAccess io, ModuleDesign module, string id, IEnumerable<SemanticSearchFieldDesign> fields)
        {
            var sources = fields.SelectMany(f => SemanticSearchText.SourceFields(module, f)).ToHashSet();
            var row = await io.GetWithOwnedRecordsAsync(module.Name, id, path => sources.Contains(path));
            if (row != null) await FillLinkDisplayTextsAsync(io, module, row, sources);
            return row;
        }

        //リンク・モジュール連携の候補値の表示名 (得意先名など) を入れる。内部読みは表示名を埋めないので、画面の LinkField と同じくリンク先を値で引く
        //(権限は通さない = docs のとおりリンク先の閲覧条件は見ない)。リンク先に無い値・表示名の無い設計はそのまま (文章には値が出る)
        static async Task FillLinkDisplayTextsAsync(ModuleDataIOInternalAccess io, ModuleDesign module, ModuleData row, IEnumerable<string> sources)
        {
            foreach (var name in sources)
            {
                var (targetModule, valueVariable, displayVariable) = module.Fields.FirstOrDefault(f => f.Name == name) switch
                {
                    LinkFieldDesign link => (link.SearchCondition.ModuleName, link.ValueVariable, link.DisplayTextVariable),
                    SelectFieldDesign select => (select.SearchCondition.ModuleName, select.ValueVariable, select.DisplayTextVariable),
                    _ => (string.Empty, string.Empty, string.Empty),
                };
                if (string.IsNullOrEmpty(targetModule) || string.IsNullOrEmpty(valueVariable) || string.IsNullOrEmpty(displayVariable)) continue;
                if (row.Fields.GetValueOrDefault(name) is not ValueFieldDataBase<string> { Value: { Length: > 0 } value } data) continue;
                if (data is LinkFieldData { DisplayText.Length: > 0 } or SelectFieldData { DisplayText.Length: > 0 }) continue;
                var displayField = new VariableName(displayVariable).FieldName.FullName;
                var condition = new SearchCondition(targetModule) { LimitCount = 1, SelectFields = [displayField] };
                condition.Condition = new FieldValueMatchCondition { SearchTargetVariable = valueVariable, Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(value) };
                var target = (await io.GetListAsync(condition, 0)).Items.FirstOrDefault();
                if (target?.Fields.GetValueOrDefault(displayField) is not ValueFieldDataBase<string> { Value: { Length: > 0 } text }) continue;
                switch (data)
                {
                    case LinkFieldData l: l.DisplayText = text; break;
                    case SelectFieldData s: s.DisplayText = text; break;
                }
            }
        }

        /// <summary>一括 INSERT の行にこのフィールドのデータが無かった (ホストの BulkAddAsync が ApplyAsync を呼んでいない) ときの警告。</summary>
        internal void WarnBulkAddNotIndexed(string moduleName)
            => _logger?.LogWarning("SemanticSearch: rows of '{Module}' were inserted in bulk without an index. Call SemanticSearchService.ApplyAsync(datas) in BulkAddAsync, or run a reindex.", moduleName);

        /// <summary>保存時の索引付けが失敗した (索引の列の書き込み失敗など。保存はロールバックされる)。</summary>
        internal void LogIndexFailed(Exception e)
            => _logger?.LogError(e, "SemanticSearch: indexing in the save failed and the save was rolled back.");

        /// <summary>
        /// 保存時の埋め込み (まとめて 1 回)。プロバイダ未設定・空文は null。失敗・<see cref="SaveEmbeddingTimeout"/> 超過 (プロバイダ側のタイムアウトも) は
        /// 警告ログを出して全部 null (文章だけ保存し、再索引で埋める)。呼び出し側の cancellationToken の中断だけは投げる。
        /// </summary>
        internal async Task<float[]?[]> EmbedForSaveAsync(List<string> texts, CancellationToken cancellationToken)
        {
            var provider = GetProvider();
            if (provider == null) return new float[]?[texts.Count];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SaveEmbeddingTimeout);
            try
            {
                //プロバイダがトークンを見なくても SaveEmbeddingTimeout で待つのをやめる (呼び出しは裏で終わる)
                return await EmbedAllAsync(provider, texts, timeout.Token).WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: embedding failed or timed out. The text is saved without a vector (run a reindex later).");
                return new float[]?[texts.Count];
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

        sealed class Job(string ownerKey, string moduleName, bool missingOnly) : IProgress<(int Processed, int Total)>
        {
            readonly object _lock = new();
            string _status = AIChatJobStatus.Running;
            int _processed;
            int _total;
            string _error = string.Empty;

            public string Id { get; } = Guid.NewGuid().ToString("N");
            public string OwnerKey { get; } = ownerKey;
            public string ModuleName { get; } = moduleName;
            public bool MissingOnly { get; } = missingOnly;
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
