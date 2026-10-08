using Codeer.LowCode.Blazor.Aggregation;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.Harness
{
    /// <summary>
    /// クライアント側の Module を実 DB のサーバー処理 (ModuleDataIO) に直結するハーネス。
    /// 読み込み・保存 (子の一覧の連鎖保存・仮 Id の解決) をコアのとおりに通す。要求は数えておく (まとめ読みの確認用)。
    /// </summary>
    public class DbClientServices : IAppInfoService, IModuleDataService
    {
        readonly DesignData _design;
        readonly Func<ModuleDataIO> _createIO;
        readonly ScriptRuntimeTypeManager _types = new();

        public DbClientServices(DesignData design, Func<ModuleDataIO> createIO)
        {
            _design = design;
            _createIO = createIO;
            Core = new Codeer.LowCode.Blazor.RequestInterfaces.Services(new NullServiceProvider(), this, this,
                new DummyNavigationService(), new DummyUIService(), Logger);
        }

        public Codeer.LowCode.Blazor.RequestInterfaces.Services Core { get; }
        public ListLogger Logger { get; } = new();

        /// <summary>GetListAsync に来た要求 (1 回の呼び出しが 1 要素)。</summary>
        public List<List<GetListRequest>> ListCalls { get; } = new();
        /// <summary>SubmitAsync に来たデータ。</summary>
        public List<List<ModuleSubmitData>> SubmitCalls { get; } = new();

        public string CurrentUserId { get; set; } = "U1";
        public ModuleData? CurrentUserData { get; set; }
        public ScriptRuntimeTypeManager GetScriptRuntimeTypeManager() => _types;
        public Task<MemoryStream?> GetResourceAsync(string resourcePath) => Task.FromResult<MemoryStream?>(null);
        public DesignData GetDesignData() => _design;

        public async Task<List<Paging<ModuleData>>> GetListAsync(List<GetListRequest> requests)
        {
            lock (ListCalls) ListCalls.Add(requests);
            var result = new List<Paging<ModuleData>>();
            foreach (var r in requests) result.Add(await _createIO().GetListAsync(r.Condition, r.PageIndex));
            return result;
        }

        public async Task<List<ModuleSubmitResult>?> SubmitAsync(List<ModuleSubmitData> data)
        {
            SubmitCalls.Add(data);
            return await _createIO().SubmitWithTransactionAsync(data);
        }

        /// <summary>モジュールを 1 件読み、クライアントの Module にする (詳細画面を開くのと同じ)。</summary>
        public async Task<Module> OpenAsync(string moduleName, string id, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            var page = await _createIO().GetListAsync(new SearchCondition
            {
                ModuleName = moduleName,
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = "Id.Value", Comparison = MatchComparison.Equal, Value = new StringValue { Value = id } }),
            }, 0);
            return await ModuleCreationService.CreateModuleAsync(Core, page.Items.Single(), layoutType);
        }

        /// <summary>新規のモジュール (詳細画面の新規作成)。</summary>
        public async Task<Module> CreateNewAsync(string moduleName, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
            => await ModuleCreationService.CreateModuleAsync(Core, new ModuleData { Name = moduleName }, layoutType);

        /// <summary>集計 (TagField の候補)。呼ばれた条件は AggregateCalls に残す。</summary>
        public List<List<AggregateCondition>> AggregateCalls { get; } = new();

        public async Task<List<AggregateResult>> AggregateAsync(List<AggregateCondition> conditions)
        {
            lock (AggregateCalls) AggregateCalls.Add(conditions);
            var result = new List<AggregateResult>();
            foreach (var c in conditions) result.Add(await _createIO().AggregateAsync(c));
            return result;
        }
        public Task<Codeer.LowCode.Blazor.DataIO.FileInfo?> UploadFile(string moduleName, string fieldName, string fileName, StreamContent content) => throw new NotImplementedException();
        public Task<MemoryStream?> DownloadFile(string moduleName, string fieldName, string id) => throw new NotImplementedException();
        public Task<MemoryStream?> GetListFileAsync(SearchCondition condition) => throw new NotImplementedException();
        public Task<List<ModuleSubmitResult>?> SubmitByFileAsync(string moduleName, StreamContent content) => throw new NotImplementedException();

        class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }
    }

    /// <summary>
    /// 1 本のスレッドで継続を順に回す同期コンテキスト (Blazor WebAssembly と同じ)。
    /// Task.Yield の継続が、いま走っている処理の後ろに並ぶことを確かめるのに使う。
    /// </summary>
    public sealed class SingleThreadContext : SynchronizationContext
    {
        readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public static void Run(Func<Task> action)
        {
            var previous = Current;
            var context = new SingleThreadContext();
            SetSynchronizationContext(context);
            try
            {
                var task = action();
                task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                foreach (var (callback, state) in context._queue.GetConsumingEnumerable()) callback(state);
                task.GetAwaiter().GetResult();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
