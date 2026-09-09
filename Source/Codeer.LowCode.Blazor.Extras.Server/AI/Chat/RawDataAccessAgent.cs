using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.Tools;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// DB を直接読んで答える Agent (<see cref="ChatClientAgent"/> + <see cref="RawDataAccessToolSet"/> + <see cref="ChartToolSet"/>)。
    /// 「先月の売上を得意先別に」「在庫が少ない品目は」のような集計・横断の質問に、SQL を組んで表とグラフで答える。
    /// <para>
    /// 名前どおり DB を生で読むので、どこまで見えるかは <see cref="RawDataAccessOptions.DataSourceName"/> に指定した
    /// AI 用 DB ユーザーの権限で決める。ログインユーザーごとの行制限 (UserRead / DataRead 条件) は効かないため、
    /// このフィールドを置くページの UserReadCondition で「誰が使えるか」を絞る。
    /// </para>
    /// <code>
    /// registry.Add("RawDataAccess", new RawDataAccessAgent(chatClientFactory, new RawDataAccessOptions
    /// {
    ///     DataSourceName = "Analytics",            // AI 用の読み取り専用ユーザーで接続するデータソース
    ///     DbAccessorFactory = () => new DbAccessor(SystemConfig.Instance.DataSources),
    ///     Modules = DesignerService.GetDesignData().Modules,
    /// }));
    /// </code>
    /// </summary>
    public class RawDataAccessAgent : ChatClientAgent
    {
        public const string DefaultSystemPrompt =
            "You are a data analyst embedded in a business web application. You answer questions about the application's data by querying its database. " +
            "Answer in the user's language, concisely, in Markdown. Show the figures you used (as a table) and a brief interpretation. " +
            "Never invent numbers: every figure must come from a query result. If the data cannot answer the question, say so and suggest what could be checked instead.";

        public RawDataAccessAgent(Func<IChatClient> clientFactory, RawDataAccessOptions dataOptions, ChatClientAgentOptions? options = null)
            : base(clientFactory, Configure(options ?? new ChatClientAgentOptions { SystemPrompt = DefaultSystemPrompt }, dataOptions))
        {
        }

        static ChatClientAgentOptions Configure(ChatClientAgentOptions options, RawDataAccessOptions dataOptions)
        {
            options.ToolSets.Add(new RawDataAccessToolSet(dataOptions));
            options.ToolSets.Add(new ChartToolSet());
            return options;
        }
    }
}
