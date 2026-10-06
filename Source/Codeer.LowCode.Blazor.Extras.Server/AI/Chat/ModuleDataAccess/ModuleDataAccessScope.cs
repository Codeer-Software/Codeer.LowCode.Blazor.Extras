using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ModuleDataAccess
{
    /// <summary>
    /// <see cref="ModuleDataAccessAgent"/> がレコードを読むときに開く、実行ユーザーの権限を持つ ModuleDataIO とその寿命。
    /// ツール呼び出し 1 回ごとに開いて閉じる (バックグラウンド実行なのでリクエストの寿命には乗れない)。
    /// owner は ModuleDataIO と DB 接続を持つもの (テンプレートの DataService) で、閉じるときに一緒に Dispose される。
    /// dbAccessor はその ModuleDataIO が使う接続。渡すと <see cref="ModuleDataAccessOptions.CommandTimeoutSeconds"/> が効く (渡さなければタイムアウトは付かない)。
    /// <code>
    /// userId => { var ds = new DataService(userId); return new ModuleDataAccessScope(ds.ModuleDataIO, ds, ds.DbAccess); }
    /// </code>
    /// </summary>
    public sealed class ModuleDataAccessScope(ModuleDataIO moduleDataIO, IAsyncDisposable? owner = null, IDbAccessor? dbAccessor = null) : IAsyncDisposable
    {
        public ModuleDataIO ModuleDataIO { get; } = moduleDataIO;

        /// <summary>ModuleDataIO が使う接続 (SQL のタイムアウトを入れる先)。null ならタイムアウトは付かない。</summary>
        public IDbAccessor? DbAccessor { get; } = dbAccessor;

        public async ValueTask DisposeAsync()
        {
            if (owner != null) await owner.DisposeAsync();
        }
    }
}
