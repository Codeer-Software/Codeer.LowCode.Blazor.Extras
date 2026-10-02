using Codeer.LowCode.Blazor.DataIO;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// <see cref="ModuleDataAccessAgent"/> がレコードを読むときに開く、実行ユーザーの権限を持つ ModuleDataIO とその寿命。
    /// ツール呼び出し 1 回ごとに開いて閉じる (バックグラウンド実行なのでリクエストの寿命には乗れない)。
    /// owner は ModuleDataIO と DB 接続を持つもの (テンプレートの DataService) で、閉じるときに一緒に Dispose される。
    /// <code>
    /// userId => { var ds = new DataService(userId); return new ModuleDataAccessScope(ds.ModuleDataIO, ds); }
    /// </code>
    /// </summary>
    public sealed class ModuleDataAccessScope(ModuleDataIO moduleDataIO, IAsyncDisposable? owner = null) : IAsyncDisposable
    {
        public ModuleDataIO ModuleDataIO { get; } = moduleDataIO;

        public async ValueTask DisposeAsync()
        {
            if (owner != null) await owner.DisposeAsync();
        }
    }
}
