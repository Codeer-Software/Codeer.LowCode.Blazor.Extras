using Codeer.LowCode.Blazor;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// WebAPI (コントローラのアクション) を記録するミドルウェア。UseRouting の直後 (認可の前) に置く
    /// (<see cref="AuditLogExtensions.UseAuditLog"/>)。認可ミドルウェアの 401/403 やアンチフォージェリの 400 も見える位置。
    /// 認証は自分で解決する (既定の認証スキームで AuthenticateAsync) ので、認証ミドルウェアより前に置いてもユーザーが入る。
    /// - 分類はアクションの <see cref="AuditAttribute"/>、名前は "Controller.Action"
    /// - 二段で書く: 操作の前に試行の行 (Result = Attempt。<see cref="AuditLogger.HasAttempt"/> の分類だけ)、
    ///   操作の後に結果の行。2 行は RequestId で結ぶ。前段が書けなければ (Strict) 操作を実行しない = 記録の無い操作は起きない
    /// - デザインの版 (<see cref="AuditEvent.DesignVersion"/>) は前段の前に決め、2 行とも同じ値を書く
    /// - 結果: 例外 = Failure (本体の権限拒否 LowCodeAccessDeniedException は Denied)、401/403 = Denied、その他 4xx/5xx = Failure。コントローラが <see cref="AuditContext"/> で上書きできる
    /// - 後段の書き込みはレスポンスの先頭が出る前 (OnStarting)。Strict で書けなければレスポンスは 500 になる
    ///   (操作自体はコミット済みのことがある = 前段の行だけが残り、結果は ILogger の Critical にある)
    /// </summary>
    public class AuditLogMiddleware
    {
        readonly RequestDelegate _next;
        readonly AuditLogger _logger;

        public AuditLogMiddleware(RequestDelegate next, AuditLogger logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.GetEndpoint();
            var action = endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action == null || !_logger.IsEnabled)
            {
                await _next(context);
                return;
            }

            var audit = context.RequestServices.GetRequiredService<AuditContext>();
            //引数で持ち回れない処理 (保存のインターセプタ・一括ファイル・メール) が対象を足せるよう、このリクエストの間だけ見えるようにする
            AuditContext.Current = audit;
            var e = audit.Event;
            e.Category = endpoint!.Metadata.GetMetadata<AuditAttribute>()?.Category ?? AuditCategory.Other;
            e.Action = $"{action.ControllerName}.{action.ActionName}";
            SetRequest(e, context);
            //デザインの版はここで 1 つに決まり、このリクエストの間は変わらない (ホストがリクエストの版を固定する)
            e.DesignVersion = _logger.GetDesignVersion(context);
            var userId = await ResolveUserIdAsync(context);

            //前段: 試行の記録。書けなければ (Strict) ここで例外 = 操作は実行されない
            await _logger.WriteAsync(new AuditEvent
            {
                Category = e.Category, Action = e.Action, Result = AuditResult.Attempt, UserId = userId,
                ClientIp = e.ClientIp, UserAgent = e.UserAgent, RequestId = e.RequestId, DesignVersion = e.DesignVersion,
            });

            var completed = false;
            async Task CompleteAsync(Exception? exception)
            {
                if (completed) return;
                completed = true;
                //後段の時刻は操作が終わった時刻 (レコードはリクエストの最初に作られるので取り直す。前段より前の時刻にしない)
                e.OccurredAtUtc = DateTime.UtcNow;
                if (string.IsNullOrEmpty(e.UserId)) e.UserId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? userId;
                //例外ハンドラ (UseExceptionHandler) がこのミドルウェアより内側にあると、例外はここまで上がらずエラー応答に変わる。
                //その応答を書く時点では例外がフィーチャーに残っているので、そこから理由を取る (ホストの並び順に依存しない)
                exception ??= context.Features.Get<IExceptionHandlerFeature>()?.Error;
                if (exception != null)
                {
                    //本体の権限拒否 (LowCodeAccessDeniedException) は 401/403 と同じ Denied。それ以外の例外は Failure
                    e.Result = exception is LowCodeAccessDeniedException ? AuditResult.Denied : AuditResult.Failure;
                    if (string.IsNullOrEmpty(e.Detail)) e.Detail = exception.Message;
                }
                else if (e.Result == AuditResult.Success)
                {
                    var status = context.Response.StatusCode;
                    if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden) e.Result = AuditResult.Denied;
                    else if (status >= StatusCodes.Status400BadRequest)
                    {
                        e.Result = AuditResult.Failure;
                        if (string.IsNullOrEmpty(e.Detail)) e.Detail = $"HTTP {status}";
                    }
                }
                //件数・補足 (AddCount / AddNote) を Detail の先頭に入れる
                e.Detail = audit.ComposeDetail();
                await _logger.WriteAsync(e);
            }

            context.Response.OnStarting(() => CompleteAsync(null));
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                //操作の失敗を記録する。ここで監査ログも書けなかった (Strict) 場合は Critical に残っているので、元の失敗をそのまま上げる
                try { await CompleteAsync(ex); } catch (AuditLogException) { }
                throw;
            }
        }

        //リクエストの情報 (接続元・RequestId)。アクション以外の場所 (外部ログインのコールバック) から記録するときも同じものを入れる
        internal static void SetRequest(AuditEvent e, HttpContext context)
        {
            e.ClientIp = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            e.UserAgent = context.Request.Headers.UserAgent.ToString();
            e.RequestId = context.TraceIdentifier;
        }

        //認証ミドルウェアより前に置かれるので、既定の認証スキームで自分で解決する (認証が無いアプリでは空)
        static async Task<string> ResolveUserIdAsync(HttpContext context)
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
                if (schemes != null && await schemes.GetDefaultAuthenticateSchemeAsync() != null)
                {
                    var result = await context.AuthenticateAsync();
                    if (result.Succeeded) context.User = result.Principal;
                }
            }
            return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }
    }
}
