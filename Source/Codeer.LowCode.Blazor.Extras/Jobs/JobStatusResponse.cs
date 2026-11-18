using System.Text.Json.Serialization;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Jobs
{
    /// <summary>
    /// 非同期で起動したジョブの状態。フィールドの GetJobStatus が返す (HTTP では GET <see cref="Fields.JobTriggerField.EndPoint"/>/{jobId} の応答)。
    /// </summary>
    public class JobStatusResponse
    {
        /// <summary>状態。</summary>
        public JobStatus Status { get; set; }

        /// <summary>Done のときの文言 (ジョブの戻り値。無ければ空)。</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Error のときのエラーの文言。</summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>Done のときに、ジョブが返したデータの JSON (Job スクリプトの args.Result)。無ければ null。</summary>
        [ScriptHide]
        public string? ResultJson { get; set; }

        /// <summary>ジョブが返したデータ (<see cref="ResultJson"/> を読んだもの。スクリプトで <c>status.Result.Total</c> のように読む)。無ければ null。通信には載せない。</summary>
        [JsonIgnore]
        public JsonObject? Result => string.IsNullOrEmpty(ResultJson) ? null : JsonObject.ToJsonObject(ResultJson);
    }
}
