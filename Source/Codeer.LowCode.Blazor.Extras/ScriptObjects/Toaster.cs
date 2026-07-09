using Codeer.LowCode.Blazor.Extras.Services;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    public class Toaster
    {
        readonly IToasterEx? _core;
        public Toaster(IToasterEx core) => _core = core;
        public virtual void Success(string s) => _core?.Success(s);
        public virtual void Info(string s) => _core?.Info(s);
        public virtual void Warn(string s) => _core?.Warn(s);
        public virtual void Error(string s) => _core?.Error(s);
    }
}
