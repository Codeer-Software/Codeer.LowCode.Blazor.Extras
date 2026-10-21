namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// JobTriggerField からホストへのジョブ実行の依頼 (POST <see cref="Fields.JobTriggerField.EndPoint"/>)。
    /// どのジョブを動かすか・待つかどうかはホストが ModuleName / FieldName のデザイン (JobTriggerFieldDesign の JobName / IsAsynchronous) から決める (クライアントはジョブ名を送らない)。
    /// 受け取ってジョブを動かすのはホストの実装 (テンプレの Controllers/JobController.cs → Extras.Server の JobProcess)。
    /// </summary>
    public class JobRunRequest
    {
        /// <summary>JobTriggerField を持つモジュールの名前。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>JobTriggerField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>
        /// ジョブに渡す JSON (1 つ)。JobTriggerField は <c>{"data":ModuleData,"submit":[ModuleSubmitData...] または null}</c> (UseSubmitData) か <c>{}</c> を送る。
        /// </summary>
        public string Arguments { get; set; } = string.Empty;
    }
}
