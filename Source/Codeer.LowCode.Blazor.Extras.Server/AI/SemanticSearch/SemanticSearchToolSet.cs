using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Codeer.LowCode.Blazor.Extras.Server.AI.Embedding;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// SemanticSearchField を置いたモジュールの行を「意味で探す」ツール (<c>search_records</c>)。
    /// 質問文を埋め込みにして、DB のベクトル検索 (pgvector / SQL Server 2025) でコサイン類似度が高い行を Id・詳細 URL・文章つきで返す。
    /// 距離計算は DB が行う。ベクトル検索に対応しない DB (SQLite 等) のモジュールは意味検索の対象にならない。
    /// SQL の集計 (RawDataAccessToolSet) と役割を分け、「似た事例」「〜のような問い合わせ」のように内容で探す質問に使わせる。
    /// 読める範囲は RawDataAccess と同じ (データソース名で絞る。行ごとの閲覧条件は効かない)。
    /// </summary>
    internal sealed class SemanticSearchToolSet : IAIChatToolSet
    {
        readonly Func<DesignData?> _design;
        readonly Func<IDbAccessor> _dbAccessorFactory;
        readonly Func<IEmbeddingProvider> _embeddingProvider;
        readonly IList<string> _dataSourceNames;
        readonly int _maxTextChars;
        readonly int _maxTop;

        /// <param name="dataSourceNames">検索を許すデータソース名 (RawDataAccessOptions.DataSourceNames)。空なら全モジュール</param>
        /// <param name="maxTextChars">1 件の文章を AI に返す最大文字数</param>
        /// <param name="maxTop">1 回に返す件数の上限</param>
        public SemanticSearchToolSet(Func<DesignData?> design, Func<IDbAccessor> dbAccessorFactory, Func<IEmbeddingProvider> embeddingProvider,
            IList<string> dataSourceNames, int maxTextChars = 1500, int maxTop = 20)
        {
            _design = design;
            _dbAccessorFactory = dbAccessorFactory;
            _embeddingProvider = embeddingProvider;
            _dataSourceNames = dataSourceNames;
            _maxTextChars = maxTextChars;
            _maxTop = maxTop;
        }

        public string GetInstructions(AIChatToolContext context)
        {
            var design = _design();
            var targets = design == null ? new() : SearchableModules(design);
            if (targets.Count == 0) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("次のモジュールは意味検索 (search_records) で「内容が似た記録」を探せます (各行を文章にして埋め込みで索引済み):");
            foreach (var (module, field, _) in targets)
            {
                var title = DesignDescriber.Title(design!, module);
                sb.Append("- ").Append(module.Name);
                if (!string.IsNullOrEmpty(title) && title != module.Name) sb.Append(" [").Append(title).Append(']');
                sb.Append(" (文章にしているフィールド: ").Append(string.Join(", ", SemanticSearchText.SourceFields(module, field).Select(n => FieldLabel(module, n)))).AppendLine(")");
            }
            sb.AppendLine("- 「似た事例は」「〜のような問い合わせ」「〜について書かれた記録」のように内容や意味で探す質問は、SQL の LIKE ではなく search_records を使ってください。件数や合計などの集計は SQL です。");
            sb.AppendLine("- search_records の結果には Id・詳細ページの URL・文章が付きます。行を挙げるときは URL で Markdown リンクを付け、score が低いものは「近いものは見つからなかった」と正直に伝えてください。");

            //同じ表は execute_sql の中でも距離計算できる ({embed:…} を質問の埋め込みに置き換える)
            sb.AppendLine("- これらの表はベクトル型の列を持ち、execute_sql の SQL の中でも意味の近さで絞り込み・並べ替えができます。SQL に `{embed:探したい内容}` と書くと、実行前にその内容の埋め込みベクトルに置き換わります (自分で数値を書かないこと)。WHERE や JOIN・集計と組み合わせたいときはこちら、単に似た記録を挙げるだけなら search_records を使ってください:");
            foreach (var (module, field, type) in targets)
                sb.Append("  - ").Append(module.Name).Append(": 表 ").Append(module.DbTable).Append(" のベクトル列 ").Append(field.DbColumnVectorSearch).Append(" (データソース ").Append(module.DataSourceName).Append(")。").AppendLine(SemanticSearchIndexReader.DialectHint(type));
            return sb.ToString();
        }

        public IEnumerable<AITool> CreateTools(AIChatToolContext context)
        {
            var design = _design();
            if (design == null || SearchableModules(design).Count == 0) yield break;
            yield return AIFunctionFactory.Create(
                ([Description("探すモジュール名 (意味検索できるモジュールの一覧にある名前)。")] string moduleName,
                 [Description("探したい内容 (自然文でよい。ユーザーの言葉をそのまま、または要点を補って)。")] string query,
                 [Description("返す件数 (既定 5)。")] int top = 5)
                    => SearchAsync(moduleName, query, top, context),
                "search_records",
                "モジュールの行を内容の意味で探し、似ている順に Id・score (0〜1)・詳細 URL・文章を返す。");
        }

        async Task<string> SearchAsync(string moduleName, string query, int top, AIChatToolContext context)
        {
            var design = _design();
            var searchable = design == null ? new() : SearchableModules(design);
            var found = searchable.Where(t => string.Equals(t.Module.Name, (moduleName ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (design == null || found.Count == 0)
                return Json(new { error = $"モジュール '{moduleName}' は意味検索できません。使えるのは: {string.Join(", ", searchable.Select(t => t.Module.Name))}" });
            var target = found[0];
            if (string.IsNullOrWhiteSpace(query)) return Json(new { error = "query が空です。" });
            top = Math.Clamp(top, 1, _maxTop);

            context.Progress.Report(Resources.AIChat_SearchingRecords);
            context.Logger?.LogInformation("AIChat search_records {Module} by {User}: {Query}", target.Module.Name, context.Request.UserName, query);
            try
            {
                var queryVector = (await _embeddingProvider().EmbedAsync(new[] { query }, context.CancellationToken))[0];

                //距離計算は DB。失敗 (拡張未導入・列の型違い等) はそのままエラーとして AI に返す (別経路で拾い直すことはしない)
                List<SemanticSearchIndexReader.ScoredEntry> scored;
                await using (var db = _dbAccessorFactory())
                    scored = await SemanticSearchIndexReader.SearchAsync(db, target.Module, target.Field, queryVector, top, context.CancellationToken);

                var urls = DesignDescriber.PageUrls(design, target.Module);
                var results = scored
                    .Select(x => new
                    {
                        id = x.Id,
                        score = Math.Round(x.Score, 3),
                        url = urls?.Detail.Replace("{Id}", x.Id),
                        text = x.Text.Length > _maxTextChars ? x.Text[.._maxTextChars] + "…" : x.Text,
                    })
                    .ToList();
                return Json(new { module = target.Module.Name, results });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e)
            {
                context.Logger?.LogWarning(e, "AIChat search_records failed for {Module}", target.Module.Name);
                return Json(new { error = e.Message });
            }
        }

        //意味検索できるモジュール = SemanticSearchField (3 列あり) を持ち、表があり、許されたデータソースにあり、そのデータソースがベクトル検索に対応する DB のもの。
        //データソース種別は DataSource 定義から見る (接続はしない)
        List<(ModuleDesign Module, SemanticSearchFieldDesign Field, DataSourceType Type)> SearchableModules(DesignData design)
        {
            var result = new List<(ModuleDesign, SemanticSearchFieldDesign, DataSourceType)>();
            var candidates = new List<(ModuleDesign Module, SemanticSearchFieldDesign Field)>();
            foreach (var name in design.Modules.GetModuleNames())
            {
                var module = design.Modules.Find(name);
                if (module == null || string.IsNullOrEmpty(module.DbTable)) continue;
                if (_dataSourceNames.Count > 0 && !_dataSourceNames.Contains(module.DataSourceName, StringComparer.OrdinalIgnoreCase)) continue;
                var field = module.Fields.OfType<SemanticSearchFieldDesign>().FirstOrDefault(f => f.HasColumns);
                if (field != null) candidates.Add((module, field));
            }
            if (candidates.Count == 0) return result;

            var db = _dbAccessorFactory();
            try
            {
                foreach (var (module, field) in candidates)
                {
                    var type = db.GetDataSource(module.DataSourceName)?.DataSourceType;
                    if (type != null && SemanticSearchIndexReader.SupportsDbSearch(type.Value)) result.Add((module, field, type.Value));
                }
            }
            finally
            {
                db.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            return result;
        }

        static string FieldLabel(ModuleDesign module, string name)
        {
            var display = (module.Fields.FirstOrDefault(f => f.Name == name) as ValueFieldDesignBase)?.DisplayName;
            return string.IsNullOrEmpty(display) ? name : display;
        }

        static readonly Regex _embedPlaceholder = new(@"\{embed:(.*?)\}", RegexOptions.Compiled | RegexOptions.Singleline);

        /// <summary>
        /// execute_sql の前処理: SQL 中の <c>{embed:探したい内容}</c> を、その内容の埋め込みベクトルのリテラル (方言ごとの書き方) に置き換える。
        /// AI は数値のベクトルを自分では書けないので、この置換で DB 側のベクトル検索を SQL の中から使えるようにする。
        /// 置き換え後は数値だけの文字列リテラルになるので、その後の SELECT 判定にはそのまま通る。プレースホルダーが無ければ何もしない。
        /// </summary>
        public async Task<string> ExpandEmbeddingsAsync(string dataSourceName, string sql, CancellationToken cancellationToken)
        {
            var matches = _embedPlaceholder.Matches(sql);
            if (matches.Count == 0) return sql;

            DataSourceType type;
            await using (var db = _dbAccessorFactory())
                type = SemanticSearchIndexReader.DataSourceTypeOf(db, dataSourceName);
            if (!SemanticSearchIndexReader.SupportsDbSearch(type))
                throw new InvalidOperationException($"データソース '{dataSourceName}' ({type}) はベクトル検索に対応していないので {{embed:…}} は使えません。");

            var texts = matches.Select(m => m.Groups[1].Value.Trim()).ToList();
            if (texts.Any(string.IsNullOrEmpty)) throw new InvalidOperationException("{embed:…} の中身が空です。探したい内容を書いてください。");
            var vectors = await _embeddingProvider().EmbedAsync(texts, cancellationToken);
            var literals = vectors.Select(v => SemanticSearchIndexReader.VectorLiteral(type, v)).ToList();

            var index = 0;
            return _embedPlaceholder.Replace(sql, _ => literals[index++]);
        }

        static readonly JsonSerializerOptions _json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        static string Json(object value) => JsonSerializer.Serialize(value, _json);
    }
}
