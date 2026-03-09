using Codeer.LowCode.Blazor.OperatingModel;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class ModuleCalendarItem
    {
        public DateTime Start { get; set; }
        public DateTime? End { get; set; }
        public bool AllDay { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public Module? Module { get; set; }
    }
}
