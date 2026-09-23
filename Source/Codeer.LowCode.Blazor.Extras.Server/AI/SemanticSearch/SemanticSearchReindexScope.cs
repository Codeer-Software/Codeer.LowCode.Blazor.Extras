using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// 再索引ジョブが使う、実行ユーザーの ModuleDataIO と DB 接続の組 (バックグラウンドで走るのでリクエストのものは使えない。ホストが開いて渡す)。
    /// <c>owner</c> はこの組を持つホスト側オブジェクト (テンプレートの DataService 等) で、ジョブの終了時に Dispose される。
    /// </summary>
    public sealed class SemanticSearchReindexScope(ModuleDataIO moduleDataIO, IDbAccessor dbAccessor, IAsyncDisposable? owner = null) : IAsyncDisposable
    {
        public ModuleDataIO ModuleDataIO { get; } = moduleDataIO;
        public IDbAccessor DbAccessor { get; } = dbAccessor;

        public async ValueTask DisposeAsync()
        {
            if (owner != null) await owner.DisposeAsync();
            else await DbAccessor.DisposeAsync();
        }
    }
}
