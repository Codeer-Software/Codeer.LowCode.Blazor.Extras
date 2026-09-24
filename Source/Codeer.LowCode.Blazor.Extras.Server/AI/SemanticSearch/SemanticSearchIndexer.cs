using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の索引を保存時に付けるサーバー側ヘルパー (PasswordHashHelper と同じ位置づけ)。
    /// <see cref="ApplyAsync"/> を <c>ModuleDataIO</c> の派生 (テンプレートの <c>CustomizedModuleDataIO.AddAsync / UpdateAsync</c>) から呼ぶと、
    /// 送られてきた文章 (クライアントのフィールドが Submit 時に組み立てたもの) に埋め込みベクトルを付けて、書き込み専用列に保存される形にする。
    /// 埋め込みは <see cref="IEmbeddingProvider"/> (Azure OpenAI / 独自。アプリの対応表で選ぶ)。未設定なら文章だけ保存し、ベクトルは null のまま (検索対象にならない)。
    /// 埋め込みの呼び出しに失敗したときも保存は止めず、ベクトル null で保存して警告ログを出す (<see cref="ReindexAsync"/> で後から埋められる)。
    /// <code>
    /// //アプリの静的な持ち物として 1 つ作る
    /// static readonly SemanticSearchIndexer _indexer = new(() => embeddingProvider, logger);
    /// //CustomizedModuleDataIO
    /// protected override async Task&lt;string&gt; AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
    /// {
    ///     await _indexer.ApplyAsync(_designData, data, isNewData: true);
    ///     return await base.AddAsync(transactionId, moduleSubmitId, data);
    /// }
    /// </code>
    /// </summary>
    public sealed class SemanticSearchIndexer
    {
        readonly Func<IEmbeddingProvider?>? _providerFactory;
        readonly ILogger? _logger;

        /// <param name="providerFactory">埋め込みプロバイダの取り方 (null か null を返すなら埋め込みなし = 文章だけ保存)。都度呼ぶので、設定の読み込み失敗を起動時に確定させずに済む</param>
        /// <param name="logger">埋め込みの失敗を記録する先 (null ならログなし)</param>
        public SemanticSearchIndexer(Func<IEmbeddingProvider?>? providerFactory, ILogger? logger = null)
        {
            _providerFactory = providerFactory;
            _logger = logger;
        }

        /// <summary>
        /// 保存前に、モジュールの各 SemanticSearchField の文章に埋め込みベクトルを付ける。
        /// 文章はデータに含まれていればそれを使い (クライアントが組み立てたもの・再索引)、無ければ新規行のときだけデータから組み立てる (一括取込など)。
        /// 更新で文章が送られていないとき (対象フィールドが変わっていない) は何もしない。
        /// 文章と一緒にベクトルも送られていれば (再索引がまとめて埋め込んだもの) そのまま使い、埋め込みは呼ばない。
        /// </summary>
        public async Task ApplyAsync(DesignData designData, ModuleData data, bool isNewData, CancellationToken cancellationToken = default)
        {
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
        /// モジュールの全行の索引を作り直す (フィールドを後から置いたとき・埋め込みモデルを変えたとき・埋め込みに失敗した行を埋めるとき)。
        /// 行は moduleDataIO で読み (実行ユーザーの読み取り権限の範囲)、ページ単位で文章を組み立て、ページ分をまとめて 1 回で埋め込み、
        /// 文章とベクトルを付けて通常の Submit で書く (書き込み権限も通常どおり効く。<see cref="ApplyAsync"/> はベクトル付きなので埋め込みを呼ばない)。
        /// 埋め込みモデルが無いときは文章だけ書き直す。埋め込みの失敗はそのまま例外にする (保存時と違い黙って null にはしない)。
        /// missingOnly はベクトルがまだ無い行だけ (書き込み専用列を db で直接読んで判定する)。progress には (書いた行数, 対象行数) を報告する。戻り値は書いた行数。
        /// ホストからの入口は <see cref="SemanticSearchReindexJobStore"/> (権限検査つきのジョブ)。
        /// </summary>
        internal async Task<int> ReindexAsync(ModuleDataIO moduleDataIO, IDbAccessor db, DesignData designData, string moduleName, bool missingOnly = false,
            IProgress<(int Processed, int Total)>? progress = null, int pageSize = 100, CancellationToken cancellationToken = default)
        {
            var module = designData.Modules.Find(moduleName) ?? throw new ArgumentException($"Module '{moduleName}' does not exist.", nameof(moduleName));
            var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).ToList();
            if (fields.Count == 0) throw new ArgumentException($"Module '{moduleName}' has no SemanticSearchField with the text, vector and vector-search columns set.", nameof(moduleName));
            var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault() ?? throw new ArgumentException($"Module '{moduleName}' has no IdField.", nameof(moduleName));

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

            var provider = _providerFactory?.Invoke();
            var count = 0;
            var total = 0;
            for (var pageIndex = 0; ; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var condition = new SearchCondition(moduleName) { LimitCount = pageSize };
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

        /// <summary>文章の埋め込み。プロバイダ未設定・空文・失敗は null。</summary>
        internal async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            IEmbeddingProvider? provider;
            try { provider = _providerFactory?.Invoke(); }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: the embedding provider could not be created. The text is saved without a vector.");
                return null;
            }
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
    }
}
