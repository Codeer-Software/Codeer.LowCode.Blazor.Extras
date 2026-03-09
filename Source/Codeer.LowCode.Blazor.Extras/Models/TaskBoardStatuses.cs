using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Models
{
    public class TaskBoardStatusDesign
    {
        public string DisplayText { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Color { get; set; } = string.Empty;

        public string BackgroundColor { get; set; } = string.Empty;

        public bool CanAdd { get; set; } = true;
    }

    public class TaskBoardStatuses : ICurrentSettingsText
    {
        public List<TaskBoardStatusDesign> Items { get; set; } = [];

        public string GetCurrentSettings() => string.Join(", ", Items.Select(s => s.DisplayText));
    }
}
