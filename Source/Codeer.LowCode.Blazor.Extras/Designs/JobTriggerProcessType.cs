using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// JobTriggerField で起動したジョブを、Web サーバーのプロセスの中で動かすか別のプロセスで動かすか。
    /// この値はホストの実行関数に渡るだけで、どう動かすかはホストが決める。
    /// </summary>
    public enum JobTriggerProcessType
    {
        [Designer(DisplayName = "$JobTriggerProcessType_NewProcess")] NewProcess,
        [Designer(DisplayName = "$JobTriggerProcessType_InProcess")] InProcess,
    }
}
