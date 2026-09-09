using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Server.Services;
using System.Collections.Concurrent;

namespace Extras.Server.AI
{
    /// <summary>
    /// AIChatField の Agent 名 → Agent の対応表 (メールの MailSenderTable と同じ位置づけ。アプリの持ち物)。
    /// AIChatField のデザインの Agent にここの名前を書く。自分の Agent を足すときはこの表に 1 行足す。
    ///   ""              = DummyAIChatAgent (AI を呼ばない。UI 確認用の既定)
    ///   "RawDataAccess" = RawDataAccessAgent (DB を直接読んで集計・グラフで答える)。AISettings (Azure OpenAI) が設定されているときだけ使える
    /// Agent は会話履歴を持つので、名前ごとに 1 つ作って使い回す。<see cref="Jobs"/> がその表を使うジョブ置き場 (プロセスに 1 つ)。
    /// </summary>
    internal static class AIChatAgentTable
    {
        static readonly ConcurrentDictionary<string, Lazy<IAIChatAgent?>> _agents = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>AIChatController が使うジョブ置き場。送信で Agent をバックグラウンド実行し、ポーリングに状態を返す。</summary>
        public static AIChatJobStore Jobs { get; } = new(Create);

        /// <summary>名前に対応する Agent (無ければ null = ジョブは error)。</summary>
        public static IAIChatAgent? Create(string name)
            => _agents.GetOrAdd(name ?? string.Empty, n => new Lazy<IAIChatAgent?>(() => CreateCore(n))).Value;

        static IAIChatAgent? CreateCore(string name) => name switch
        {
            "" => new DummyAIChatAgent(),
            "RawDataAccess" => CreateRawDataAccess(),
            _ => null,
        };

        //RawDataAccess が読むデータソースは appsettings の AIChat:RawDataAccessDataSource (既定 SampleSQLite)。
        //本番では AI 用の読み取り専用 DB ユーザーで接続するデータソースを指す (何が読めるかは DB 側の権限で決める)
        static IAIChatAgent? CreateRawDataAccess()
        {
            var chatClientFactory = AIChatClientFactory.CreateAzureOpenAI(SystemConfig.Instance.AISettings);
            if (chatClientFactory == null) return null;
            return new RawDataAccessAgent(
                chatClientFactory,
                () => new DbAccessor(SystemConfig.Instance.DataSources),
                DesignerService.GetDesignData().Modules,
                new RawDataAccessOptions { DataSourceName = SystemConfig.Instance.AIChat.RawDataAccessDataSource },
                new ChatClientAgentOptions
            {
                SystemPrompt = RawDataAccessAgent.DefaultSystemPrompt,
                LoggerFactory = LoggerFactory.Create(b => b.AddConsole()),
            });
        }
    }
}
