using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Models
{
    public class TaskBoardStatuses : ICurrentSettingsText
    {
        public List<TaskBoardStatusDesign> Items { get; set; } = [];

        public string GetCurrentSettings() => string.Join(", ", Items.Select(s => s.DisplayText));
    }
}
