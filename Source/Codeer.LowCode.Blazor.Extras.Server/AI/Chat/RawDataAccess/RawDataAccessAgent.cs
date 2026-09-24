using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// アプリの設計を読み、DB を直接読んで答える Agent。
    /// 設計参照 (list_modules / describe_module / read_document)、DB 参照 (get_schema / execute_sql)、SVG グラフ (render_chart)、
    /// 意味検索 (search_records。<see cref="SemanticSearchService"/> を渡し、設計に SemanticSearchField があるとき) のツールを持ち、
    /// 「先月の売上を得意先別に」「在庫が少ない品目は」のような集計・横断の質問に、業務語をモジュール定義で解いてから SQL を組み、表とグラフで答える。
    /// 会話の基盤 (モデル呼び出し・履歴・逐次表示・Markdown → HTML) はライブラリ内部の会話エンジンに委譲する。
    /// <para>
    /// 名前どおり DB を生で読むので、どこまで見えるかは <see cref="RawDataAccessOptions.DataSourceNames"/> に指定した
    /// AI 用 DB ユーザーの権限で決める。ログインユーザーごとの行制限 (UserRead / DataRead 条件) は効かないため、
    /// このフィールドを置くページの UserReadCondition で「誰が使えるか」を絞る。
    /// </para>
    /// <code>
    /// new RawDataAccessAgent(
    ///     chatClientFactory,                                          // IChatClient の作り方 (アプリの責務)
    ///     () => new DbAccessor(SystemConfig.Instance.DataSources),    // IDbAccessor の作り方 (SQL ごとに作って捨てる)
    ///     () => DesignerService.GetDesignData(),                      // デザイン定義 (ホットリロードで変わるので都度。null 可)
    ///     folder => DesignDataFileManager.GetResourceTexts(dir, folder, ".md", ".txt")   // 補足文書 (デザインの Resources/{DocumentFolder}。null 可)
    ///         .Select(e => new AIChatDocument(e.Name, e.Text)).ToList(),
    ///     new RawDataAccessOptions { DataSourceNames = { "Analytics" } }); // AI 用の読み取り専用ユーザーで接続するデータソース
    /// </code>
    /// </summary>
    public sealed class RawDataAccessAgent : IAIChatAgent
    {
        readonly ChatClientAgent _agent;

        /// <param name="clientFactory">モデル (IChatClient) の作り方。プロバイダの選択と認証はアプリの責務</param>
        /// <param name="dbAccessorFactory">データソースへ接続する IDbAccessor の作り方 (SQL 1 回ごとに作って捨てる)</param>
        /// <param name="design">デザイン定義の取り方 (ホットリロードで変わるので都度呼ぶ)。null なら設計参照ツールを付けない</param>
        /// <param name="documents">補足文書の取り方。引数は AIChatField のデザインの DocumentFolder (Resources からの相対パス)。null なら文書を渡さない</param>
        /// <param name="options">データソース名・上限値・プロンプト・履歴の設定</param>
        /// <param name="loggerFactory">実行した SQL の監査ログとモデル呼び出しのログの出力先。null ならログなし</param>
        /// <param name="semanticSearch">意味検索 (SemanticSearchField のサーバー側入口。保存時の索引付けに使っているものと同じ)。渡すと、設計に SemanticSearchField があり埋め込みプロバイダが設定されているとき意味検索ツール (search_records / {embed:…}) が付く。null なら意味検索なし</param>
        public RawDataAccessAgent(Func<IChatClient> clientFactory, Func<IDbAccessor> dbAccessorFactory,
            Func<DesignData?>? design, Func<string, IReadOnlyList<AIChatDocument>>? documents,
            RawDataAccessOptions options, ILoggerFactory? loggerFactory = null,
            SemanticSearchService? semanticSearch = null)
        {
            var conversation = new ChatClientAgentOptions
            {
                SystemPrompt = options.SystemPrompt,
                MaxToolCallRoundsPerReply = options.MaxToolCallRoundsPerReply,
                MaxHistoryTurns = options.MaxHistoryTurns,
                KeepToolResultsForTurns = options.KeepToolResultsForTurns,
                MaxHistoryCharacters = options.MaxHistoryCharacters,
                HistoryRetention = options.HistoryRetention,
                StreamPartialReplies = options.StreamPartialReplies,
                LoggerFactory = loggerFactory,
            };
            if (design != null || documents != null) conversation.ToolSets.Add(new DesignKnowledgeToolSet(design, documents));
            var rawDataAccess = new RawDataAccessToolSet(dbAccessorFactory, design, options);
            conversation.ToolSets.Add(rawDataAccess);
            if (design != null && semanticSearch != null)
            {
                var toolSet = new SemanticSearchToolSet(design, dbAccessorFactory, semanticSearch.GetProvider, options.DataSourceNames);
                conversation.ToolSets.Add(toolSet);
                //execute_sql の {embed:…} を質問の埋め込みに置き換える (DB 側のベクトル検索を SQL の中から使う)
                rawDataAccess.SqlPreprocessor = toolSet.ExpandEmbeddingsAsync;
            }
            conversation.ToolSets.Add(new ChartToolSet());
            _agent = new ChatClientAgent(clientFactory, conversation);
        }

        public Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
            => _agent.ReplyAsync(request, progress, cancellationToken);
    }
}
