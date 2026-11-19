namespace Codeer.LowCode.Blazor.Extras.JobTrigger
{
    /// <summary>
    /// JobTriggerField からホストへのジョブ実行の依頼 (POST <see cref="Fields.JobTriggerField.EndPoint"/>)。
    /// どのジョブを動かすか・待つかどうか・実行の口に渡すプロセスの指定はサーバーが ModuleName / FieldName のデザイン (JobTriggerFieldDesign の JobName / IsAsynchronous / ProcessType) から読む (クライアントはジョブ名を送らない)。
    /// 受け取るのは共通の API の Extras.Server の JobTriggerApi で、ホストが渡した実行関数でジョブを動かす。
    /// </summary>
    public class JobTriggerRunRequest
    {
        /// <summary>JobTriggerField を持つモジュールの名前。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>JobTriggerField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// ジョブに渡す JSON (1 つ)。JobTriggerField は <c>{"data":ModuleData,"submit":[ModuleSubmitData...] または null}</c> (UseSubmitData) か <c>{}</c> を送る。
        /// デザインの UseSubmitData が false なら、サーバーはこの値を使わずジョブに <c>{}</c> を渡す。
        /// </summary>
        public string Arguments { get; set; } = string.Empty;
    }
}
