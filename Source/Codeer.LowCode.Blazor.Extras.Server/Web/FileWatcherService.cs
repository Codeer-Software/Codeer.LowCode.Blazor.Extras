using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace Codeer.LowCode.Blazor.Extras.Server.Web
{
    /// <summary>
    /// Watches the design file directory and notifies connected clients
    /// via <see cref="HotReloadHub"/> when a design zip is updated.
    /// </summary>
    public class FileWatcherService : IHostedService
    {
        readonly IHubContext<HotReloadHub> _hubContext;
        readonly string _designFileDirectory;
        FileSystemWatcher? _fileWatcher;

        public FileWatcherService(IHubContext<HotReloadHub> hubContext, string designFileDirectory)
        {
            _hubContext = hubContext;
            _designFileDirectory = designFileDirectory;
        }

        public virtual async Task StartAsync(CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_designFileDirectory);
            _fileWatcher = new FileSystemWatcher
            {
                Path = _designFileDirectory,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                Filter = "*.zip"
            };
            _fileWatcher.Changed += OnChanged;
            _fileWatcher.EnableRaisingEvents = true;

            await Task.CompletedTask;
        }

        public virtual async Task StopAsync(CancellationToken cancellationToken)
        {
            _fileWatcher?.Dispose();
            await Task.CompletedTask;
        }

        async void OnChanged(object sender, FileSystemEventArgs e)
            => await _hubContext.Clients.All.SendAsync("ExecuteHotReload");
    }
}
