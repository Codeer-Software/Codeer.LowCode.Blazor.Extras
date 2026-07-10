using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class Toaster
    {
        readonly IToastService _core;
        public Toaster(IToastService core) => _core = core;
        public void Success(string s) => _core.Success(s);
        public void Info(string s) => _core.Info(s);
        public void Warn(string s) => _core.Warn(s);
        public void Error(string s) => _core.Error(s);
    }
}
