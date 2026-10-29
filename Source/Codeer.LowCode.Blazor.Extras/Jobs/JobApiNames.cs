namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>ジョブの機能が共通の API (ExtrasTransport / Extras.Server の JobApi) で使う機能名と操作名。</summary>
    public static class JobApiNames
    {
        public const string Feature = "Job";

        /// <summary>ジョブの実行。要求 JobRunRequest、応答 JobRunResponse。</summary>
        public const string Run = "Run";

        /// <summary>非同期で起動したジョブの状態。要求 JobStatusRequest、応答 JobStatusResponse (知らない Id・結果を忘れた Id・他のユーザーのジョブは応答なし)。</summary>
        public const string Status = "Status";
    }
}
