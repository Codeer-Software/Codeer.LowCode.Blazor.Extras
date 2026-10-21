namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// ジョブ実行の結果。待たずに起動したときは <see cref="Started"/> だけ。
    /// 待ったときは <see cref="Succeeded"/> で成否、成功なら <see cref="Message"/>、失敗なら <see cref="Error"/> が画面に出る。
    /// </summary>
    public class JobRunResponse
    {
        /// <summary>ジョブが成功した (待ったときだけ)。</summary>
        public bool Succeeded { get; set; }

        /// <summary>失敗したときに画面に出す文言 (空ならフィールドの既定の文言)。</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>待たずに起動した (JobTriggerFieldDesign.IsAsynchronous) とき true。そのとき結果は返らない。</summary>
        public bool Started { get; set; }

        /// <summary>成功したときに画面に出す文言 (空ならフィールドの既定の文言)。</summary>
        public string Message { get; set; } = string.Empty;
    }
}
