using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess
{
    /// <summary>
    /// アプリの設計を読み、DB を直接読んで答える Agent。中に <see cref="ChatClientAgent"/> を持ち、
    /// 設計参照 (list_modules / describe_module / read_document)、DB 参照 (get_schema / execute_sql)、SVG グラフ (render_chart) のツールを組み込んで委譲する。
    /// 「先月の売上を得意先別に」「在庫が少ない品目は」のような集計・横断の質問に、業務語をモジュール定義で解いてから SQL を組み、表とグラフで答える。
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
    ///     folder => AIChatDocuments.Read(folder),                     // 補足文書 (デザインの Resources/{DocumentFolder}/*.md。null 可)
    ///     new RawDataAccessOptions { DataSourceNames = { "Analytics" } }); // AI 用の読み取り専用ユーザーで接続するデータソース
    /// </code>
    /// </summary>
    public sealed class RawDataAccessAgent : IAIChatAgent
    {
        public const string DefaultSystemPrompt =
            "あなたは業務 Web アプリケーションに組み込まれたデータアナリストです。アプリの設計 (モジュール定義) と補足文書で業務の意味を確かめ、データベースに問い合わせて、データに関する質問に答えます。" +
            "質問に出てくる用語 (売上、在庫など) は、補足文書に定義があればその定義に従い、設計の候補値 (状態コード等) で条件に落としてください。定義を確かめずに列名だけで解釈しないこと。" +
            "ユーザーの言語で、簡潔に、Markdown で答えてください。使った数字は表で示し、短い解釈を添えてください。" +
            "数字を作らないこと。すべての数値はクエリ結果に基づくものだけにしてください。データで答えられない質問には、その旨と代わりに確認できることを伝えてください。";

        readonly ChatClientAgent _agent;

        /// <param name="clientFactory">モデル (IChatClient) の作り方。プロバイダの選択と認証はアプリの責務</param>
        /// <param name="dbAccessorFactory">データソースへ接続する IDbAccessor の作り方 (SQL 1 回ごとに作って捨てる)</param>
        /// <param name="design">デザイン定義の取り方 (ホットリロードで変わるので都度呼ぶ)。null なら設計参照ツールを付けない</param>
        /// <param name="documents">補足文書の取り方。引数は AIChatField のデザインの DocumentFolder (Resources からの相対パス)。null なら文書を渡さない</param>
        /// <param name="dataOptions">データソース名と上限値</param>
        /// <param name="options">会話 Agent の設定。省略時は <see cref="DefaultSystemPrompt"/> で作る</param>
        public RawDataAccessAgent(Func<IChatClient> clientFactory, Func<IDbAccessor> dbAccessorFactory,
            Func<DesignData?>? design, Func<string, IReadOnlyList<AIChatDocument>>? documents,
            RawDataAccessOptions dataOptions, ChatClientAgentOptions? options = null)
        {
            options ??= new ChatClientAgentOptions { SystemPrompt = DefaultSystemPrompt };
            if (design != null || documents != null) options.ToolSets.Add(new DesignKnowledgeToolSet(design, documents));
            options.ToolSets.Add(new RawDataAccessToolSet(dbAccessorFactory, design, dataOptions));
            options.ToolSets.Add(new ChartToolSet());
            _agent = new ChatClientAgent(clientFactory, options);
        }

        public Task<AIChatReply> ReplyAsync(AIChatAgentRequest request, IAIChatProgress progress, CancellationToken cancellationToken)
            => _agent.ReplyAsync(request, progress, cancellationToken);
    }
}
