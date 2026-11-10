using Microsoft.Extensions.Configuration;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// WebApiField の接続先 1 つ (appsettings の WebApi.Connections の 1 要素)。URL と認証はサーバーだけが持ち、ブラウザには出ない。
    /// </summary>
    public sealed class WebApiConnection
    {
        internal const int DefaultTimeoutSeconds = 30;

        /// <summary>接続先の名前 (WebApiFieldDesign.ConnectionName で指す)。</summary>
        public string Name { get; internal init; } = string.Empty;

        /// <summary>呼び出す URL の土台 (例 https://api.example.com/v1/)。フィールドの Path はこの下だけを指せる。</summary>
        public string BaseUrl { get; internal init; } = string.Empty;

        /// <summary>認証方式の名前 (IWebApiAuthenticator.Name)。空なら None。</summary>
        public string Authentication { get; internal init; } = string.Empty;

        /// <summary>応答を待つ秒数 (既定 30)。</summary>
        public int TimeoutSeconds { get; internal init; } = DefaultTimeoutSeconds;

        /// <summary>この接続先の設定のセクション全体 (方式ごとの設定はこの中の Authentication と同じ名前の子)。</summary>
        internal IConfiguration Configuration { get; init; } = new ConfigurationBuilder().Build();

        /// <summary>認証方式の設定 (Authentication と同じ名前の子セクション) を T として読む。無ければ既定値の T。</summary>
        public T GetAuthenticationSettings<T>() where T : new()
            => (string.IsNullOrEmpty(Authentication) ? default : Configuration.GetSection(Authentication).Get<T>()) ?? new T();
    }
}
