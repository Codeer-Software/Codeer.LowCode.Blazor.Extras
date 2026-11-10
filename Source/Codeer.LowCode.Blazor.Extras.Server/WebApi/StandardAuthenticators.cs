using Codeer.LowCode.Blazor.Extras.Server.Properties;
using System.Net.Http.Headers;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    //認証なし (Authentication が空のときもこれ)
    sealed class NoneAuthenticator : IWebApiAuthenticator
    {
        public string Name => "None";

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
            => next(request);
    }

    //接続先の "ApiKey": { "HeaderName": "X-Api-Key", "Key": "...", "QueryName": "" }。QueryName があればクエリに、無ければヘッダーに付ける
    sealed class ApiKeyAuthenticationSettings
    {
        public string HeaderName { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string QueryName { get; set; } = string.Empty;
    }

    sealed class ApiKeyAuthenticator : IWebApiAuthenticator
    {
        public string Name => "ApiKey";

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            var settings = connection.GetAuthenticationSettings<ApiKeyAuthenticationSettings>();
            if (string.IsNullOrEmpty(settings.Key)) throw new WebApiCallException(string.Format(Resources.WebApi_AuthenticationSettingsInvalid, connection.Name));
            if (!string.IsNullOrEmpty(settings.QueryName))
            {
                var uri = request.RequestUri!;
                var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
                request.RequestUri = new Uri(uri.GetLeftPart(UriPartial.Path) + uri.Query + separator + Uri.EscapeDataString(settings.QueryName) + "=" + Uri.EscapeDataString(settings.Key));
            }
            else
            {
                if (string.IsNullOrEmpty(settings.HeaderName)) throw new WebApiCallException(string.Format(Resources.WebApi_AuthenticationSettingsInvalid, connection.Name));
                request.Headers.Remove(settings.HeaderName);
                request.Headers.TryAddWithoutValidation(settings.HeaderName, settings.Key);
            }
            return next(request);
        }
    }

    //接続先の "Bearer": { "Token": "..." } (固定のトークン)
    sealed class BearerAuthenticationSettings
    {
        public string Token { get; set; } = string.Empty;
    }

    sealed class BearerAuthenticator : IWebApiAuthenticator
    {
        public string Name => "Bearer";

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            var settings = connection.GetAuthenticationSettings<BearerAuthenticationSettings>();
            if (string.IsNullOrEmpty(settings.Token)) throw new WebApiCallException(string.Format(Resources.WebApi_AuthenticationSettingsInvalid, connection.Name));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            return next(request);
        }
    }

    //接続先の "Basic": { "UserName": "...", "Password": "..." }
    sealed class BasicAuthenticationSettings
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    sealed class BasicAuthenticator : IWebApiAuthenticator
    {
        public string Name => "Basic";

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, WebApiConnection connection, Func<HttpRequestMessage, Task<HttpResponseMessage>> next, CancellationToken cancellationToken)
        {
            var settings = connection.GetAuthenticationSettings<BasicAuthenticationSettings>();
            if (string.IsNullOrEmpty(settings.UserName)) throw new WebApiCallException(string.Format(Resources.WebApi_AuthenticationSettingsInvalid, connection.Name));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(settings.UserName + ":" + settings.Password)));
            return next(request);
        }
    }
}
