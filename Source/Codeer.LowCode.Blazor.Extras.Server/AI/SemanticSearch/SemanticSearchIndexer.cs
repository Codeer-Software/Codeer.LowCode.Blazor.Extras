using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField の索引を保存時に付けるサーバー側ヘルパー (PasswordHashHelper と同じ位置づけ)。
    /// <see cref="ApplyAsync"/> を <c>ModuleDataIO</c> の派生 (テンプレートの <c>CustomizedModuleDataIO.AddAsync / UpdateAsync</c>) から呼ぶと、
    /// 送られてきた文章 (クライアントのフィールドが Submit 時に組み立てたもの) に埋め込みベクトルを付けて、書き込み専用列に保存される形にする。
    /// 埋め込みモデルの作り方 (IEmbeddingGenerator) はアプリの責務 (IChatClient と同じ)。未設定なら文章だけ保存し、ベクトルは null のまま (検索対象にならない)。
    /// 埋め込みの呼び出しに失敗したときも保存は止めず、ベクトル null で保存して警告ログを出す (<see cref="ReindexAsync"/> で後から埋められる)。
    /// <code>
    /// //アプリの静的な持ち物として 1 つ作る
    /// static readonly SemanticSearchIndexer _indexer = new(() => embeddingGenerator, logger);
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
        readonly Func<IEmbeddingGenerator<string, Embedding<float>>?>? _embeddingGeneratorFactory;
        readonly ILogger? _logger;

        /// <param name="embeddingGeneratorFactory">埋め込みモデルの取り方 (null か null を返すなら埋め込みなし = 文章だけ保存)</param>
        /// <param name="logger">埋め込みの失敗を記録する先 (null ならログなし)</param>
        public SemanticSearchIndexer(Func<IEmbeddingGenerator<string, Embedding<float>>?>? embeddingGeneratorFactory, ILogger? logger = null)
        {
            _embeddingGeneratorFactory = embeddingGeneratorFactory;
            _logger = logger;
        }

        /// <summary>
        /// 保存前に、モジュールの各 SemanticSearchField の文章に埋め込みベクトルを付ける。
        /// 文章はデータに含まれていればそれを使い (クライアントが組み立てたもの・再索引)、無ければ新規行のときだけデータから組み立てる (一括取込など)。
        /// 更新で文章が送られていないとき (対象フィールドが変わっていない) は何もしない。
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
                    text = sent.Text;
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
        /// 行は moduleDataIO で読み (実行ユーザーの読み取り権限の範囲)、行ごとに文章を組み立てて通常の Submit で書く
        /// (そこで <see cref="ApplyAsync"/> が埋め込みを付ける = 書き込み権限も通常どおり効く)。戻り値は書いた行数。
        /// </summary>
        public static async Task<int> ReindexAsync(ModuleDataIO moduleDataIO, DesignData designData, string moduleName, int pageSize = 100, CancellationToken cancellationToken = default)
        {
            var module = designData.Modules.Find(moduleName) ?? throw new ArgumentException($"Module '{moduleName}' does not exist.", nameof(moduleName));
            var fields = module.Fields.OfType<SemanticSearchFieldDesign>().Where(f => f.HasColumns).ToList();
            if (fields.Count == 0) throw new ArgumentException($"Module '{moduleName}' has no SemanticSearchField with both columns set.", nameof(moduleName));
            var idField = module.Fields.OfType<IdFieldDesign>().FirstOrDefault() ?? throw new ArgumentException($"Module '{moduleName}' has no IdField.", nameof(moduleName));

            var count = 0;
            for (var pageIndex = 0; ; pageIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var condition = new SearchCondition(moduleName) { LimitCount = pageSize };
                condition.SortConditions.Add(new SortCondition { Variable = $"{idField.Name}.Value" });
                var page = await moduleDataIO.GetListAsync(condition, pageIndex);
                var submits = new List<ModuleSubmitData>();
                foreach (var row in page.Items)
                {
                    if (!row.Fields.TryGetValue(idField.Name, out var id) || id == null) continue;
                    var update = new ModuleData { Name = moduleName };
                    update.Fields[idField.Name] = id;
                    foreach (var field in fields)
                        update.Fields[field.Name] = new SemanticSearchFieldData { Text = SemanticSearchText.Build(designData, module, row, field) };
                    submits.Add(new ModuleSubmitData { ModuleName = moduleName, Id = (id as IdFieldData)?.Value ?? string.Empty, Update = { update } });
                }
                if (submits.Count > 0)
                {
                    var results = await moduleDataIO.SubmitWithTransactionAsync(submits);
                    var error = results.FirstOrDefault(r => !string.IsNullOrEmpty(r.ExceptionMessage))?.ExceptionMessage;
                    if (error != null) throw new InvalidOperationException(error);
                    count += submits.Count;
                }
                if (pageIndex + 1 >= page.PageCount || page.Items.Count == 0) break;
            }
            return count;
        }

        /// <summary>文章の埋め込み。モデル未設定・空文・失敗は null。</summary>
        internal async Task<float[]?> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            IEmbeddingGenerator<string, Embedding<float>>? generator;
            try { generator = _embeddingGeneratorFactory?.Invoke(); }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: the embedding generator could not be created. The text is saved without a vector.");
                return null;
            }
            if (generator == null) return null;
            try
            {
                var embeddings = await generator.GenerateAsync(new[] { text }, cancellationToken: cancellationToken);
                return embeddings.Count == 0 ? null : embeddings[0].Vector.ToArray();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                _logger?.LogWarning(e, "SemanticSearch: embedding failed. The text is saved without a vector (run a reindex later).");
                return null;
            }
        }
    }
}
