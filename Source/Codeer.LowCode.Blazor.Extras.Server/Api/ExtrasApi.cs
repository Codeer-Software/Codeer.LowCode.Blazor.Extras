using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.Server.Api
{
    /// <summary>
    /// Extras の機能の要求を、機能名で <see cref="IExtrasApiHandler"/> に振り分ける。
    /// Web ホストは 1 本の Controller (テンプレの Controllers/ExtrasController.cs。認証・CSRF・監査の属性はそこに書く) から、
    /// デスクトップは ExtrasTransport.Handler からプロセス内で呼ぶ。どの機能を出すか・何を渡すかは <see cref="Handlers"/> に並べる (テンプレの Extras/ExtrasApiTable.cs)。
    /// </summary>
    public sealed class ExtrasApi
    {
        public List<IExtrasApiHandler> Handlers { get; } = new();

        /// <summary>
        /// 要求を実行する。機能が <see cref="Handlers"/> に無ければ null (Web ホストは 404 にする)。
        /// 監査は実行の前に分類 (振り分け先の GetAuditCategory) と操作名 "機能.操作" を決め、試行の行がある分類ならそこで書く (AuditContext.StartAsync)。
        /// 対象・失敗は振り分け先が入れる。機能の中の例外 (権限の拒否など) はそのまま上げる。
        /// </summary>
        public async Task<ExtrasResponse?> ExecuteAsync(ExtrasRequest request, ExtrasApiContext context)
        {
            var handler = Handlers.FirstOrDefault(e => e.Feature == request.Feature);
            if (handler == null) return null;

            context.Audit ??= AuditContext.Current;
            if (context.Audit != null) await context.Audit.StartAsync(handler.GetAuditCategory(request.Action), $"{request.Feature}.{request.Action}");

            var result = await handler.ExecuteAsync(request.Action, request.Payload, context);
            return new ExtrasResponse { Payload = result == null ? string.Empty : JsonConverterEx.SerializeObject(result, indented: false) };
        }

        /// <summary>操作ごとの要求を Payload から読む (空なら既定値)。</summary>
        public static T ReadPayload<T>(string payload) where T : new()
            => (string.IsNullOrEmpty(payload) ? default : JsonConverterEx.DeserializeObject<T>(payload)) ?? new T();

        /// <summary>知らない操作名の例外 (クライアントとサーバーの版の食い違い)。</summary>
        public static Exception UnknownAction(string feature, string action)
            => new InvalidOperationException($"Unknown action: {feature}.{action}");
    }
}
