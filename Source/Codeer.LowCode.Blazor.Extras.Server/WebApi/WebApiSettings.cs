using Microsoft.Extensions.Configuration;

namespace Codeer.LowCode.Blazor.Extras.Server.WebApi
{
    /// <summary>
    /// appsettings の "WebApi" セクション。WebApiField が呼べる接続先の並び (Connections)。
    /// 接続先ごとの認証の設定は、接続先の中の Authentication と同じ名前の子セクションに置く (方式ごとに形が違う)。
    /// </summary>
    public sealed class WebApiSettings
    {
        /// <summary>接続先。名前 (Name) は WebApiFieldDesign.ConnectionName で指す (大文字小文字は区別しない)。</summary>
        internal List<WebApiConnection> Connections { get; } = new();

        /// <summary>"WebApi" セクションから読む (<c>WebApiSettings.Load(configuration.GetSection("WebApi"))</c>)。セクションが無ければ接続先なし。</summary>
        public static WebApiSettings Load(IConfiguration section)
        {
            var settings = new WebApiSettings();
            foreach (var e in section.GetSection("Connections").GetChildren())
            {
                settings.Connections.Add(new WebApiConnection
                {
                    Name = e["Name"] ?? string.Empty,
                    BaseUrl = e["BaseUrl"] ?? string.Empty,
                    Authentication = e["Authentication"] ?? string.Empty,
                    TimeoutSeconds = int.TryParse(e["TimeoutSeconds"], out var timeout) ? timeout : WebApiConnection.DefaultTimeoutSeconds,
                    Configuration = e,
                });
            }
            return settings;
        }
    }
}
