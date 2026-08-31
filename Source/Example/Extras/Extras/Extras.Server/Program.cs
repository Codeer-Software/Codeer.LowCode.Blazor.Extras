using Codeer.LowCode.Blazor.Extras;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.License;
using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Server.Services;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.Excel;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Web;
using Microsoft.AspNetCore.SignalR;
using Extras.Server.Services.DataChangeHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.ResponseCompression;
using PdfSharp.Fonts;
using System.Globalization;
using System.Text.Json.Serialization;

//load dll.
typeof(AITextAnalyzerField).ToString();

ExtrasServerInitializer.Initialize();

var builder = WebApplication.CreateBuilder(args);

//Development 以外の環境名 (Approval 等のテスト用プロファイル) でも Client の wwwroot / Blazor フレームワークファイルを配信する
builder.WebHost.UseStaticWebAssets();

LicenseManager.DomainLicense = builder.Configuration.GetSection("DomainLicense").Get<string>() ?? string.Empty;
LicenseManager.IsAutoUpdate = builder.Configuration.GetSection("IsLicenseAutoUpdate").Get<bool>();
SystemConfig.Instance.UseHotReload = builder.Configuration.GetSection("UseHotReload").Get<bool>();
SystemConfig.Instance.CanScriptDebug = builder.Configuration.GetSection("CanScriptDebug").Get<bool>();
SystemConfig.Instance.DataSources = builder.Configuration.GetSection("DataSources").Get<DataSource[]>() ?? [];
//ファイル保存先は種類ごとのセクションを FileStorageTable が読む (FileSystemStorages / AzureBlobStorages / S3Storages)
SystemConfig.Instance.FileStorages = FileStorageTable.Create(builder.Configuration);
SystemConfig.Instance.DataChangeHistoryTableInfo = builder.Configuration.GetSection("DataChangeHistoryTableInfo").Get<DataChangeHistoryTableInfo[]>() ?? [];
SystemConfig.Instance.TemporaryFileTableInfo = builder.Configuration.GetSection("TemporaryFileTableInfo").Get<TemporaryFileTableInfo[]>() ?? [];
SystemConfig.Instance.DesignFileDirectory = builder.Configuration["DesignFileDirectory"] ?? string.Empty;
SystemConfig.Instance.FontFileDirectory = builder.Configuration["FontFileDirectory"] ?? string.Empty;
SystemConfig.Instance.Mail = builder.Configuration.GetSection("Mail").Get<MailConfig>() ?? new();
//メールのプロバイダ設定はそれぞれ独立したセクション (使うものだけ書けばよい)
SystemConfig.Instance.Smtp = builder.Configuration.GetSection("Smtp").Get<SmtpSettings>() ?? new();
SystemConfig.Instance.GraphApi = builder.Configuration.GetSection("GraphApi").Get<GraphApiSettings>() ?? new();
SystemConfig.Instance.SendGrid = builder.Configuration.GetSection("SendGrid").Get<SendGridSettings>() ?? new();
SystemConfig.Instance.Gmail = builder.Configuration.GetSection("Gmail").Get<GmailSettings>() ?? new();
SystemConfig.Instance.AISettings = builder.Configuration.GetSection("AISettings").Get<AISettings>() ?? new();
SystemConfig.Instance.DataSources.ToList().ForEach(e => e.ConnectionString = builder.Configuration.GetConnectionString(e.Name) ?? string.Empty);

GlobalFontSettings.FontResolver = new CustomFontResolver(SystemConfig.Instance.FontFileDirectory);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddControllers()
      .AddJsonOptions(options =>
      {
          options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
          options.JsonSerializerOptions.Converters.AddJsonConverters();
      });

if (SystemConfig.Instance.UseHotReload)
{
    builder.Services.AddSignalR();
    builder.Services.AddHostedService(sp => new FileWatcherService(sp.GetRequiredService<IHubContext<HotReloadHub>>(), SystemConfig.Instance.DesignFileDirectory));
}

// Compress dynamic responses (e.g. the design data fetched on every reload).
// Brotli/Gzip are decoded by the browser's native network stack, so this adds
// no decompression cost to the WASM runtime.
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(["application/octet-stream"]);
});

//Localize
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[]
    {
        new CultureInfo("ja-JP")
    };
    //set neutral as default
    options.DefaultRequestCulture = new RequestCulture(string.Empty);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    options.RequestCultureProviders.Add(new CustomRequestCultureProvider(async context =>
    {
        //localization by the request header
        var userLanguages = context.Request.Headers["Accept-Language"].ToString();
        var firstLanguage = userLanguages.Split(',').FirstOrDefault();
        if (firstLanguage == "ja") firstLanguage = "ja-JP";

        return await Task.FromResult(new ProviderCultureResult(firstLanguage));
    }));
});

builder.Services.AddScoped<DataService>();

//デモ用の簡易ログイン (パスワードなしのユーザー切替。AccountController / login.html)。
//承認フローなど操作ユーザーが必要な機能をサンプルで確認するためのもので、実運用の認証ではない
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => options.Cookie.Name = "ExtrasExample.Auth");

var app = builder.Build();

app.UseResponseCompression();
app.UseRequestLocalization();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

//未ログインでアプリのルートを開いたらデモログインページへ (簡易実装)
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/" && context.User.Identity?.IsAuthenticated != true)
    {
        context.Response.Redirect("/login.html");
        return;
    }
    await next();
});

if (SystemConfig.Instance.UseHotReload)
{
    app.MapHub<HotReloadHub>("/hot_reload_hub");
}

app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

// Exception handling.
app.UseExceptionHandlerSendToFront();
app.Run();
