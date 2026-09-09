namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// 名前 → Agent の対応表。アプリは起動時に使う Agent を登録し、AIChatField 側はデザインの Agent 名で選ぶ。
    /// 名前の大文字小文字は区別しない。空文字 (既定) の登録が無いときは、最初に登録した Agent を既定とみなす。
    /// <code>
    /// builder.Services.AddSingleton(new AIChatAgentRegistry()
    ///     .Add("", new MyDefaultAgent())
    ///     .Add("RawDataAccess", new RawDataAccessAgent(...)));
    /// builder.Services.AddSingleton&lt;AIChatJobStore&gt;(sp => new(sp.GetRequiredService&lt;AIChatAgentRegistry&gt;()));
    /// </code>
    /// </summary>
    public class AIChatAgentRegistry : IAIChatAgentResolver
    {
        readonly Dictionary<string, IAIChatAgent> _agents = new(StringComparer.OrdinalIgnoreCase);
        IAIChatAgent? _first;

        public AIChatAgentRegistry Add(string agentName, IAIChatAgent agent)
        {
            _agents[agentName ?? string.Empty] = agent;
            _first ??= agent;
            return this;
        }

        public IReadOnlyCollection<string> Names => _agents.Keys;

        public IAIChatAgent? Resolve(string agentName)
        {
            agentName ??= string.Empty;
            if (_agents.TryGetValue(agentName, out var agent)) return agent;
            return agentName.Length == 0 ? _first : null;
        }
    }
}
