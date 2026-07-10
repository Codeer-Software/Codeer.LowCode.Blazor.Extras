namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// Toast notification abstraction used by the built-in services and script objects.
    /// Replace the default <see cref="ToastService"/> by registering your own implementation in DI.
    /// </summary>
    public interface IToastService
    {
        void Clear();
        void Success(string s);
        void Warn(string s);
        void Info(string s);
        void Error(string s);
    }
}
