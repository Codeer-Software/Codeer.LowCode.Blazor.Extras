using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// アプリの設計を読み、レコードを「実行ユーザーの権限で」読んで答える Agent。
    /// 設計参照 (list_modules / describe_module / read_document)、レコード参照 (find_records / get_record / aggregate_records / cross_tab)、
    /// 意味検索 (search_records。<see cref="SemanticSearchService"/> を渡し、設計に SemanticSearchField があるとき)、SVG グラフ (render_chart) のツールを持つ。
    /// 行の読み出しは本体の ModuleDataIO (GetListAsync)、集計は本体の集計 API (AggregateAsync / CrossTabBuilder) を通るので、モジュールの閲覧権限 (UserRead)・行の条件 (DataRead)・
    /// 項目の読み取り権限 (PermissionField)・論理削除・アプリアクセス条件が画面と同じに効く。SQL は書かせない。
    /// 会話の基盤 (モデル呼び出し・履歴・逐次表示・Markdown → HTML) はライブラリ内部の会話エンジンに委譲する。
    /// <para>
    /// 生 SQL で DB を読む <see cref="RawDataAccess.RawDataAccessAgent"/> との使い分け: 行単位の統制が要る (ユーザーごとに見える行が違う・列の読み取り権限がある) なら
    /// こちら。自由な集計と横断 (JOIN・ウィンドウ関数・設計に無い表) が要り、見える範囲を DB ユーザーで決めてよいなら RawDataAccessAgent。
    /// </para>
    /// <code>
    /// new ModuleDataAccessAgent(
    ///     chatClientFactory,                                          // IChatClient の作り方 (アプリの責務)
    ///     userId => { var ds = new DataService(userId); return Task.FromResult(new ModuleDataAccessScope(ds.ModuleDataIO, ds)); },
    ///                                                                 // そのユーザーの権限を持つ ModuleDataIO (ツール呼び出しごとに開いて閉じる)
    ///     () => DesignerService.GetDesignData(),                      // デザイン定義 (ホットリロードで変わるので都度)
    ///     folder => DesignDataFileManager.GetResourceTexts(dir, folder, ".md", ".txt")   // 補足文書 (デザインの Resources/{DocumentFolder}。null 可)
    ///         .Select(e => new AIChatDocument(e.Name, e.Text)).ToList(),
    ///     new ModuleDataAccessOptions());
    /// </code>
    /// </summary>
    public sealed class ModuleDataAccessAgent : IAIChatAgent
    {
        readonly ChatClientAgent _agent;

        /// <param name="clientFactory">モデル (IChatClient) の作り方。プロバイダの選択と認証はアプリの責務</param>
        /// <param name="openScope">
        /// ユーザー Id → そのユーザーの権限を持つ ModuleDataIO (<see cref="ModuleDataAccessScope"/>)。ツール呼び出しごとに開いて閉じる。
        /// ユーザー Id は <see cref="AIChatAgentRequest.UserName"/> (= AIChatService.StartAsync の ownerKey。テンプレートはログインユーザーの Id)
        /// </param>
        /// <param name="design">デザイン定義の取り方 (ホットリロードで変わるので都度呼ぶ)。設計参照ツールと、項目の型・候補値の解決に使う</param>
        /// <param name="documents">補足文書の取り方。引数は AIChatField のデザインの DocumentFolder (Resources からの相対パス)。null なら文書を渡さない</param>
        /// <param name="options">上限値・プロンプト・履歴の設定</param>
        /// <param name="loggerFactory">ツール呼び出しの実行ログとモデル呼び出しのログの出力先 (ILogger。監査ログ (AuditLog) ではない)。null ならログなし</param>
        /// <param name="semanticSearch">意味検索 (SemanticSearchField のサーバー側入口。保存時の索引付けに使っているものと同じ)。渡すと、設計に SemanticSearchField があり埋め込みプロバイダが設定されているとき search_records が付く。
        /// 検索は本体の一覧検索の条件 (SemanticMatchCondition) として走るので、行の条件・項目の読み取り権限が効く。null なら意味検索なし</param>
        public ModuleDataAccessAgent(Func<IChatClient> clientFactory, Func<string, Task<ModuleDataAccessScope>> openScope,
            Func<DesignData?> design, Func<string, IReadOnlyList<AIChatDocument>>? documents,
            ModuleDataAccessOptions options, ILoggerFactory? loggerFactory = null, SemanticSearchService? semanticSearch = null)
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
            conversation.ToolSets.Add(new DesignKnowledgeToolSet(design, documents));
            conversation.ToolSets.Add(new ModuleDataAccessToolSet(openScope, design, options, semanticSearch == null ? null : semanticSearch.GetProvider));
            conversation.ToolSets.Add(new ChartToolSet());
            _agent = new ChatClientAgent(clientFactory, conversation);
        }

        public Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
            => _agent.ReplyAsync(request, progress, cancellationToken);
    }
}
