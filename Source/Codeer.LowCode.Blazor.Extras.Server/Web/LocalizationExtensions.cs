using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Server.Web
{
    public static class LocalizationExtensions
    {
        /// <summary>
        /// 画面の言語をブラウザの Accept-Language で決める (ja なら日本語、それ以外は中立 = 英語)。
        /// builder.Services に登録し、パイプラインでは app.UseRequestLocalization() を呼ぶ。
        /// </summary>
        public static IServiceCollection AddLowCodeLocalization(this IServiceCollection services)
        {
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            services.Configure<RequestLocalizationOptions>(options =>
            {
                var supportedCultures = new[] { new CultureInfo("ja-JP") };
                //既定は中立 (英語)
                options.DefaultRequestCulture = new RequestCulture(string.Empty);
                options.SupportedCultures = supportedCultures;
                options.SupportedUICultures = supportedCultures;
                options.RequestCultureProviders.Add(new CustomRequestCultureProvider(context =>
                {
                    //先頭の言語 (q 値は落とす)
                    var firstLanguage = context.Request.Headers.AcceptLanguage.ToString().Split(',').FirstOrDefault()?.Split(';')[0].Trim();
                    if (firstLanguage == "ja") firstLanguage = "ja-JP";
                    return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(firstLanguage));
                }));
            });
            return services;
        }
    }
}
