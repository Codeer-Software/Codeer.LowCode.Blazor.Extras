using System.Net;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// HTTP communication abstraction used by the built-in services and script objects.
    /// Replace the default <see cref="HttpService"/> by registering your own implementation in DI.
    /// </summary>
    public interface IHttpService
    {
        Task<TValue?> GetFromJsonAsync<TValue>(string url, bool loading = true) where TValue : class;
        Task<Stream?> GetFromStreamAsync(string url, bool loading = true);
        Task<HttpResponseMessage?> GetAsync(string? requestUri, bool loading = true);
        Task<HttpResponseMessage?> PostAsync(string url, HttpContent data, bool loading = true);
        Task<bool> PostAsJsonAsync<TValue>(string url, TValue data, bool loading = true);
        Task<TResult?> PostAsJsonAsync<TValue, TResult>(string url, TValue data, bool loading = true) where TResult : class;
        Task<TResult?> PostContentAsJsonAsync<TResult>(string requestUri, HttpContent? content, bool loading = true) where TResult : class;
        Task<HttpResponseMessage?> PostContent(string requestUri, HttpContent? content, bool loading = true);
        Task<HttpResponseMessage?> PostAsJsonReturnHttpResponseAsync<TValue>(string url, TValue data, bool loading = true);
        Task<HttpResponseMessage?> PutAsync(string url, HttpContent data, bool loading = true);
        Task<HttpResponseMessage?> DeleteAsync(string url, bool loading = true);
        IDisposable AddChecker(Action<HttpStatusCode, string> check);
    }
}
