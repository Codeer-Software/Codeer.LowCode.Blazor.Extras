namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// JobTriggerField からサーバーへのジョブ実行の依頼 (POST {EndPoint}/run)。
    /// サーバーはホストのジョブモード (dotnet LowCodeApp.Server.dll job &lt;JobName&gt; &lt;Arguments&gt;) を別プロセスで起動し、終わるまで待って <see cref="JobRunResponse"/> を返す。
    /// </summary>
    public class JobRunRequest
    {
        /// <summary>ジョブ名 (ホストのジョブモードのジョブ名)。</summary>
        public string JobName { get; set; } = string.Empty;

        /// <summary>
        /// ジョブに渡す引数の JSON (1 つ)。JobTriggerField は <c>{"submit":[ModuleSubmitData...]}</c> / <c>{"data":ModuleData}</c> / <c>{}</c> のどれかを送る。
        /// </summary>
        public string Arguments { get; set; } = string.Empty;

        /// <summary>true ならジョブが終わるまで待って結果を返す。false なら起動したらすぐ返す (<see cref="JobRunResponse.Started"/> = true。失敗はサーバーのログ)。</summary>
        public bool Wait { get; set; } = true;
    }
}
