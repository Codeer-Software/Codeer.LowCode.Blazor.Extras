namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>送信失敗のログ出力 (スクリプトが戻り値を見ていなくても失敗を追えるようにする)。</summary>
    internal static class MailSendLogger
    {
        public static async Task LogFailuresAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services? services, MailSendResult result)
        {
            if (result.IsSuccess || services == null) return;
            var detail = string.Join(" / ", result.Failures.Take(5).Select(e => string.IsNullOrEmpty(e.To) ? e.Error : $"{e.To}: {e.Error}"));
            await services.Logger.Error($"Mail send failed ({result.Failures.Count}/{result.TotalCount}): {detail}");
        }
    }
}
