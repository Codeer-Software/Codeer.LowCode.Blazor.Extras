using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.WebApi;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// 共通の API で受ける WebApiField の呼び出し。接続先・メソッド・パスはデザインの WebApiField から読む (クライアントからは受け取らない)。
    /// そのフィールドに書けるユーザーだけが呼べる。監査にはこの呼び出し 1 件 (誰が・どのモジュールのどの接続先を・相手の状態コード) が載る。
    /// </summary>
    public sealed class WebApiApi : IExtrasApiHandler
    {
        readonly WebApiClient _client;

        /// <param name="settings">接続先 (appsettings の "WebApi"。<see cref="WebApiSettings.Load"/>)。</param>
        /// <param name="authenticators">独自の認証方式 (無ければ標準の None / ApiKey / Bearer / Basic / OAuth2ClientCredentials だけ)。</param>
        public WebApiApi(WebApiSettings settings, params IWebApiAuthenticator[] authenticators)
            => _client = new WebApiClient(settings, authenticators);

        //テスト用 (送信を偽のハンドラで受ける)
        internal WebApiApi(WebApiClient client) => _client = client;

        public string Feature => WebApiNames.Feature;

        //試行の行を書くかはメソッドで決まる (Get は読み取り、それ以外は書き込み) ので、デザインを読んでから決め直す
        public AuditCategory GetAuditCategory(string action) => action switch
        {
            WebApiNames.Execute => AuditCategory.DataRead,
            _ => AuditCategory.Other,
        };

        public async Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context)
        {
            switch (action)
            {
                case WebApiNames.Execute:
                    return await ExecuteCoreAsync(ExtrasApi.ReadPayload<WebApiExecuteRequest>(payload), context);
                default:
                    throw ExtrasApi.UnknownAction(Feature, action);
            }
        }

        async Task<WebApiExecuteResponse> ExecuteCoreAsync(WebApiExecuteRequest request, ExtrasApiContext context)
        {
            var design = context.DesignData.Modules.Find(request.ModuleName)?.Fields.FirstOrDefault(e => e.Name == request.FieldName) as WebApiFieldDesign
                ?? throw new InvalidOperationException($"{request.ModuleName}.{request.FieldName} is not a WebApiField.");

            //拒否された試行にも「どのモジュールのどの接続先か」を残すため、権限検査の前に監査の対象を登録する
            var audit = context.Audit;
            if (audit != null)
            {
                if (design.Method != WebApiMethod.Get) await audit.StartAsync(AuditCategory.DataWrite, $"{Feature}.{WebApiNames.Execute}");
                audit.AddTarget(request.ModuleName, null, $"WebApi:{design.Method}:{design.ConnectionName}");
            }

            //そのモジュールとフィールドに書けるユーザーだけ (JobTriggerField と同じ軸。書けなければ例外)
            await context.ModuleDataIO.AuthorizationChecker.CheckFieldUserWriteAsync(request.ModuleName, request.FieldName);

            //ここから先は DB を使わない。相手を待つ間に接続を握らないよう先にプールへ返す
            await context.DbAccess.DisposeAsync();

            var response = await _client.ExecuteAsync(design.ConnectionName, design.Method, design.Path, request.Arguments, context.Logger);
            if (audit != null)
            {
                audit.AddNote("StatusCode", response.StatusCode.ToString());
                if (response.StatusCode < 200 || 299 < response.StatusCode)
                {
                    audit.Fail(string.IsNullOrEmpty(response.ErrorMessage) ? $"HTTP {response.StatusCode}" : response.ErrorMessage);
                }
            }
            return response;
        }
    }
}
