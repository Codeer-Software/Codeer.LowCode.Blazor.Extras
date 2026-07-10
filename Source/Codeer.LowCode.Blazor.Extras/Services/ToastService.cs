using Sotsera.Blazor.Toaster;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// Default <see cref="IToastService"/> implementation using Sotsera.Blazor.Toaster.
    /// </summary>
    public class ToastService : IToastService
    {
        readonly IToaster _toaster;

        public ToastService(IToaster toaster) => _toaster = toaster;

        public void Clear() => _toaster.Clear();

        /// <summary>
        /// Clears all visible toasts (including long-lived error toasts) before showing
        /// the success toast, so a stale error does not remain next to a success message.
        /// </summary>
        public void Success(string s)
        {
            _toaster.Clear();
            _toaster.Success(s);
        }

        public void Warn(string s) => _toaster.Warning(s);

        public void Info(string s) => _toaster.Info(s);

        /// <summary>
        /// Errors stay visible longer (30s) so they are not missed.
        /// </summary>
        public void Error(string s)
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
