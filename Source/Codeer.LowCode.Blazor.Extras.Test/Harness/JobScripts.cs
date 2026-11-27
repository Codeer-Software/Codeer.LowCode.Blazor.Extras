using Codeer.LowCode.Blazor.Jobs;

namespace Codeer.LowCode.Blazor.Extras.Test.Harness
{
    /// <summary>
    /// デザイナで作った Job をホストと同じ約束 (戻り値 = 成功の文言・失敗は例外) で動かす (本体の JobExecutor.RunAsync を呼ぶだけ)。
    /// </summary>
    public static class JobScripts
    {
        public static async Task<string?> RunAsync(JobExecutionContext context, string jobName, string[] args)
        {
            var result = await JobExecutor.RunAsync(context, jobName, args, new ListLogger());
            if (!result.Succeeded) throw new InvalidOperationException(result.Error);
            return result.Message;
        }
    }
}
