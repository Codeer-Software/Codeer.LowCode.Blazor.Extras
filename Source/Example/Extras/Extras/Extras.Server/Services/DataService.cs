using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.DbAccess;

namespace Extras.Server.Services
{
    public class DataService : IAuthenticationContext, IAsyncDisposable
    {
        public DbAccessor DbAccess { get; }
        public TemporaryFileManager TemporaryFileManager { get; }
        public CustomizedModuleDataIO ModuleDataIO { get; }

        readonly IHttpContextAccessor? _httpContextAccessor;
        readonly string? _fixedUserId;

        public DataService(IHttpContextAccessor? httpContextAccessor = null)
        {
            _httpContextAccessor = httpContextAccessor;
            DbAccess = new DbAccessor(SystemConfig.Instance.DataSources);
            TemporaryFileManager = new TemporaryFileManager(DbAccess, SystemConfig.Instance.TemporaryFileTableInfo, SystemConfig.Instance.FileStorages);
            ModuleDataIO = new CustomizedModuleDataIO(DesignerService.GetDesignData(), this, DbAccess, TemporaryFileManager);
        }

        /// <summary>リクエストの外 (バックグラウンドのジョブ) で、そのユーザーの権限のまま使うための DataService。</summary>
        public DataService(string userId) : this(httpContextAccessor: null)
            => _fixedUserId = userId;

        //デモログイン (AccountController) が設定した Cookie 認証のユーザー Id
        public async Task<string> GetCurrentUserIdAsync()
        {
            await Task.CompletedTask;
            if (_fixedUserId != null) return _fixedUserId;
            return _httpContextAccessor?.HttpContext?.User
                .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
        }

        public async ValueTask DisposeAsync()
            => await DbAccess.DisposeAsync();
    }
}
