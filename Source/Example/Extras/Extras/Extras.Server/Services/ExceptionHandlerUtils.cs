using System.Net;
using Microsoft.AspNetCore.Diagnostics;

namespace Extras.Server.Services
{
    public static class ExceptionHandlerUtils
    {
        public static void UseExceptionHandlerSendToFront(this WebApplication app)
        {
            app.UseExceptionHandler(errorApp =>
            {
                errorApp.Run(async context =>
                {
                    var exceptionHandlerPathFeature =
                        context.Features.Get<IExceptionHandlerPathFeature>();
                    if (exceptionHandlerPathFeature == null) return;

                    var ex = exceptionHandlerPathFeature.Error;
                    context.Response.ContentType = "text/plain";
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    await context.Response.WriteAsync(ex.GetMessages());
                });
            });
        }

        static string GetMessages(this Exception? ex)
        {
            var list = new List<string>();
            while (ex != null)
            {
                list.Add(ex.Message);
                ex = ex.InnerException;
            }
            return string.Join(Environment.NewLine, list);
        }
    }
}
