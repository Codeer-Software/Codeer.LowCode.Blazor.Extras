namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat
{
    /// <summary>
    /// AIChatField が指定した Agent 名 (<see cref="Designs.AIChatFieldDesign.Agent"/>) から <see cref="IAIChatAgent"/> を選ぶ。
    /// 空文字は「既定の Agent」。見つからなければ null を返し、ジョブは「Agent が未登録」の失敗になる。
    /// 標準実装は <see cref="AIChatAgentRegistry"/>。
    /// </summary>
    public interface IAIChatAgentResolver
    {
        IAIChatAgent? Resolve(string agentName);
    }
}
