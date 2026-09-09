using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// アプリの設計を読み、DB を直接読んで答える Agent。
    /// 設計参照 (list_modules / describe_module / read_document)、DB 参照 (get_schema / execute_sql)、SVG グラフ (render_chart) のツールを持ち、
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
        public RawDataAccessAgent(Func<IChatClient> clientFactory, Func<IDbAccessor> dbAccessorFactory,
            Func<DesignData?>? design, Func<string, IReadOnlyList<AIChatDocument>>? documents,
            RawDataAccessOptions options, ILoggerFactory? loggerFactory = null)
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
            conversation.ToolSets.Add(new RawDataAccessToolSet(dbAccessorFactory, design, options));
            conversation.ToolSets.Add(new ChartToolSet());
            _agent = new ChatClientAgent(clientFactory, conversation);
        }

        public Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
            => _agent.ReplyAsync(request, progress, cancellationToken);
    }
}
