using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras
{
    public static class ExtrasServerInitializer
    {
        public static void Initialize()
        {
            //load dll.
            typeof(TaskBoardFieldDesign).ToString();
        }
    }
}
