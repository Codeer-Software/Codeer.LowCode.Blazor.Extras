using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>デザインに置かれた JobTriggerField の問い合わせ。ホストのジョブ API は、これでデザインに書かれたジョブ名 (呼び名) だけを受け付ける。</summary>
    public static class JobTriggerFields
    {
        /// <summary>デザインのどれかのモジュールの JobTriggerField の JobName と一致するか (大文字小文字は区別しない)。</summary>
        public static bool IsDeclared(DesignData designData, string jobName)
            => !string.IsNullOrWhiteSpace(jobName) && designData.Modules.ToList()
                .SelectMany(e => e.Fields.OfType<JobTriggerFieldDesign>())
                .Any(e => string.Equals(e.JobName.Trim(), jobName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
