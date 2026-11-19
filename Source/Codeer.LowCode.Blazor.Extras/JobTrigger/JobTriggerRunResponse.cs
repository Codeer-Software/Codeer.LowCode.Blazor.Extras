namespace Codeer.LowCode.Blazor.Extras.JobTrigger
{
    /// <summary>
    /// ジョブ実行の結果。待ったときは <see cref="Succeeded"/> で成否、成功なら <see cref="Message"/>、失敗なら <see cref="Error"/> が画面に出る。
    /// 待たずに起動したときは <see cref="Started"/> と <see cref="JobId"/>。結果はフィールドの GetJobStatus で問い合わせる (<c>GET {EndPoint}/{JobId}</c> が <see cref="JobTriggerStatusResponse"/> を返す)。
    /// </summary>
    public class JobTriggerRunResponse
    {
        /// <summary>ジョブが成功した (待ったときだけ)。</summary>
        public bool Succeeded { get; set; }

        /// <summary>失敗したときに画面に出す文言 (空ならフィールドの既定の文言)。</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>待たずに起動した (JobTriggerFieldDesign.IsAsynchronous) とき true。そのとき結果は <see cref="JobId"/> で問い合わせる。</summary>
        public bool Started { get; set; }

        /// <summary>
        /// 非同期で起動した (<see cref="Started"/>) ときのジョブの Id。結果はフィールドの GetJobStatus で問い合わせる (<c>GET {EndPoint}/{JobId}</c> の <see cref="JobTriggerStatusResponse"/>)。
        /// 空なら問い合わせられない。
        /// </summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>成功したときに画面に出す文言 (空ならフィールドの既定の文言)。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>待って成功したときに、ジョブが返したデータの JSON (Job スクリプトの args.Result)。無ければ null。フィールドの Result になる。</summary>
        public string? ResultJson { get; set; }
    }
}
