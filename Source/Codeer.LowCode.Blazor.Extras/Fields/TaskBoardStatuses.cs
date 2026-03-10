using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class TaskBoardStatus
    {
        public string DisplayText { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string Color { get; set; } = string.Empty;

        public string BackgroundColor { get; set; } = string.Empty;

        public bool CanAdd { get; set; } = true;

        internal string EffectiveValue => string.IsNullOrEmpty(Value) ? DisplayText : Value;
    }

    public class TaskBoardStatuses : ICurrentSettingsText
    {
        public List<TaskBoardStatus> Items { get; set; } = [];

        public string GetCurrentSettings() => string.Join(", ", Items.Select(s => s.DisplayText));
    }

    public class TaskBoardItem
    {
        public string StatusValue { get; set; } = string.Empty;
        public int SortIndex { get; set; }
        public Module? Module { get; set; }
    }
}
