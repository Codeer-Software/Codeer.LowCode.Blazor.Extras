using Sotsera.Blazor.Toaster;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    public class ToasterEx : IToasterEx
    {
        readonly IToaster _toaster;

        public ToasterEx(IToaster toaster) => _toaster = toaster;

        public virtual void Clear() => _toaster.Clear();

        public virtual void Success(string s)
        {
            _toaster.Clear();
            _toaster.Success(s);
        }

        public virtual void Warn(string s) => _toaster.Warning(s);

        public virtual void Info(string s) => _toaster.Info(s);

        public virtual void Error(string s)
        {
            _toaster.Error(s, null, config =>
            {
                config.MaximumOpacity = 100;
                config.VisibleStateDuration = 1000 * 30;
                config.ShowTransitionDuration = 10;
                config.HideTransitionDuration = 500;
            });
        }
    }
}
