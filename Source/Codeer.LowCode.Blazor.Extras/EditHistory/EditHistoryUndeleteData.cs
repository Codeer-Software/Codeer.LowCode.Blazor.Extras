using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 論理削除の取り消しの依頼。EditHistoryField / EditHistoryRestoreButtonField が Submit の ExtendedData に載せ、
    /// サーバーの EditHistoryRecorder が base の Submit の前に ModuleDataIO.UndeleteAsync で戻す
    /// (同じトランザクション = 復活した行の Update と一緒に確定する)。
    /// </summary>
    public class EditHistoryUndeleteData : JsonAbstract
    {
        public EditHistoryUndeleteData() : base(typeof(EditHistoryUndeleteData).FullName!) { }

        public List<EditHistoryUndeleteTarget> Targets { get; set; } = new();
    }

    /// <summary>取り消す行 (モジュール名と Id)。</summary>
    public class EditHistoryUndeleteTarget
    {
        public string ModuleName { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
    }
}
