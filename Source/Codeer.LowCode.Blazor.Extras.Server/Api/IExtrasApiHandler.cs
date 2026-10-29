namespace Codeer.LowCode.Blazor.Extras.Server.Api
{
    /// <summary>
    /// 共通の API (<see cref="ExtrasApi"/>) で 1 つの機能 (メール・ジョブなど) の操作を受けるもの。
    /// ホストが使う機能だけを ExtrasApi.Handlers に並べる (並べていない機能の要求は届かない)。
    /// </summary>
    public interface IExtrasApiHandler
    {
        /// <summary>機能の名前 (ExtrasRequest.Feature と一致させる。MailApiNames.Feature 等)。</summary>
        string Feature { get; }

        /// <summary>操作の監査ログの分類 (旧 Controller の [Audit] 属性に当たる。実行の前に決まる)。記録しない操作は Other。</summary>
        AuditLog.AuditCategory GetAuditCategory(string action);

        /// <summary>
        /// 操作を実行して応答を返す。null = 返すものが無い (クライアントには応答なしとして届く)。知らない操作は例外 (<see cref="ExtrasApi.UnknownAction"/>)。
        /// 要求は <see cref="ExtrasApi.ReadPayload{T}"/> で読む。監査の対象・失敗は context.Audit に入れる。
        /// </summary>
        Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context);
    }
}
