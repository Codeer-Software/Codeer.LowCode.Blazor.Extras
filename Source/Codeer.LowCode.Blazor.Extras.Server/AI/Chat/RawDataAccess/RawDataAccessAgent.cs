using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// DB を直接読んで答える Agent。中に <see cref="ChatClientAgent"/> を持ち、スキーマ取得・SQL 実行・SVG グラフのツールを組み込んで委譲する。
    /// 「先月の売上を得意先別に」「在庫が少ない品目は」のような集計・横断の質問に、SQL を組んで表とグラフで答える。
    /// <para>
    /// 名前どおり DB を生で読むので、どこまで見えるかは <see cref="RawDataAccessOptions.DataSourceName"/> に指定した
    /// AI 用 DB ユーザーの権限で決める。ログインユーザーごとの行制限 (UserRead / DataRead 条件) は効かないため、
    /// このフィールドを置くページの UserReadCondition で「誰が使えるか」を絞る。
    /// </para>
    /// <code>
    /// new RawDataAccessAgent(
    ///     chatClientFactory,                                          // IChatClient の作り方 (アプリの責務)
    ///     () => new DbAccessor(SystemConfig.Instance.DataSources),    // IDbAccessor の作り方 (SQL ごとに作って捨てる)
    ///     DesignerService.GetDesignData().Modules,                    // 表と列に業務名を添える (null 可)
    ///     new RawDataAccessOptions { DataSourceName = "Analytics" }); // AI 用の読み取り専用ユーザーで接続するデータソース
    /// </code>
    /// </summary>
    public sealed class RawDataAccessAgent : IAIChatAgent
    {
        public const string DefaultSystemPrompt =
            "あなたは業務 Web アプリケーションに組み込まれたデータアナリストです。アプリのデータベースに問い合わせて、データに関する質問に答えます。" +
            "ユーザーの言語で、簡潔に、Markdown で答えてください。使った数字は表で示し、短い解釈を添えてください。" +
            "数字を作らないこと。すべての数値はクエリ結果に基づくものだけにしてください。データで答えられない質問には、その旨と代わりに確認できることを伝えてください。";

        readonly ChatClientAgent _agent;

        /// <param name="clientFactory">モデル (IChatClient) の作り方。プロバイダの選択と認証はアプリの責務</param>
        /// <param name="dbAccessorFactory">データソースへ接続する IDbAccessor の作り方 (SQL 1 回ごとに作って捨てる)</param>
        /// <param name="modules">モジュール定義。表と列に業務上の名前を添えてスキーマを説明する。null でも動く</param>
        /// <param name="dataOptions">データソース名と上限値</param>
        /// <param name="options">会話 Agent の設定。省略時は <see cref="DefaultSystemPrompt"/> で作る</param>
        public RawDataAccessAgent(Func<IChatClient> clientFactory, Func<IDbAccessor> dbAccessorFactory, IModuleDesigns? modules,
            RawDataAccessOptions dataOptions, ChatClientAgentOptions? options = null)
        {
            options ??= new ChatClientAgentOptions { SystemPrompt = DefaultSystemPrompt };
            options.ToolSets.Add(new RawDataAccessToolSet(dbAccessorFactory, modules, dataOptions));
            options.ToolSets.Add(new ChartToolSet());
            _agent = new ChatClientAgent(clientFactory, options);
        }

        public Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
            => _agent.ReplyAsync(request, progress, cancellationToken);
    }
}
