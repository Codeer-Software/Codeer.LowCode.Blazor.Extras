using Extras.Server.Services;

namespace Extras.Server.Jobs
{
    /// <summary>
    /// ジョブ (バッチ) 1 つ。このフォルダに実装クラス (引数なし public コンストラクタ) を足せば、
    /// dotnet Extras.Server.dll job &lt;Name&gt; で呼べるようになる。失敗は例外で投げる (終了コード 1 になる)。
    /// </summary>
    public interface IJob
    {
        /// <summary>コマンドラインで指定する名前 (大文字小文字は区別しない)。</summary>
        string Name { get; }

        /// <summary>data はこのジョブ 1 回分の DB アクセス (Web のリクエストと同じ DataService)。args はジョブ名の後ろのコマンドライン引数。</summary>
        Task RunAsync(DataService data, string[] args);
    }
}
