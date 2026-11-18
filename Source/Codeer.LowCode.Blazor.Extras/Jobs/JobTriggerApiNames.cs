namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>ジョブの機能が共通の API (ExtrasTransport / Extras.Server の JobTriggerApi) で使う機能名と操作名。</summary>
    public static class JobTriggerApiNames
    {
        public const string Feature = "Job";

        /// <summary>ジョブの実行。要求 JobTriggerRunRequest、応答 JobTriggerRunResponse。</summary>
        public const string Run = "Run";

        /// <summary>非同期で起動したジョブの状態。要求 JobTriggerStatusRequest、応答 JobTriggerStatusResponse (知らない Id・結果を忘れた Id・他のユーザーのジョブは応答なし)。</summary>
        public const string Status = "Status";
    }
}
