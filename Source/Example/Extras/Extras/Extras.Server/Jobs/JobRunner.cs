using Codeer.LowCode.Blazor.DbAccess;
using Extras.Server.AI;
using Extras.Server.Services;

namespace Extras.Server.Jobs
{
    /// <summary>
    /// このアプリのジョブ (IJob) の一覧と実行。Program.cs が dotnet Extras.Server.dll job &lt;ジョブ名&gt; [引数...] のときに
    /// Codeer.LowCode.Blazor.Extras.Server.Jobs.JobMode.RunAsync(args, Names, RunAsync) で呼ぶ。
    /// 引数の --stdin・標準入出力・終了コード (成功 0 / 失敗 1 / ジョブが無い 2) は JobMode が持ち、ここは DB アクセス (DataService) とジョブを組み立てるだけ。
    /// </summary>
    public static class JobRunner
    {
        /// <summary>このアセンブリの IJob の名前 (引数なし public コンストラクタのクラス)。</summary>
        public static IReadOnlyList<string> Names
            => CreateJobs().Select(e => e.Name).ToList();

        /// <summary>ジョブを 1 つ動かす。name は Names のどれか、args はジョブ名の後ろの引数。戻り値は成功したときの文言 (画面に出る)、失敗は例外。</summary>
        public static async Task<string?> RunAsync(IConfiguration config, string name, string[] args)
        {
            //ロガーはジョブの後に閉じて出し切る (標準出力の最後の行は JobMode が書く成功の文言)
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
            //意味検索の警告ログ (埋め込みの失敗など) を標準出力に出す
            SemanticSearchIndex.Service.LoggerFactory = loggerFactory;
            //実行した SQL とパラメータを標準出力に出す
            if (config.GetSection("SqlLog").Get<bool>()) DbAccessor.SqlLog = Console.WriteLine;

            if (!File.Exists(Path.Combine(SystemConfig.Instance.DesignFileDirectory, "App.zip")))
            {
                throw new FileNotFoundException($"DesignFileDirectory に App.zip がありません: {SystemConfig.Instance.DesignFileDirectory}");
            }

            //権限の条件はこのユーザーで判定される。空なら匿名 (ログインが要るデザインでは読み書きできない)。画面のボタンから起動したときは押したユーザー (JobProcess が環境変数で上書きする)
            var userId = config["Jobs:UserId"] ?? string.Empty;
            await using var data = new DataService(userId);
            var job = CreateJobs().First(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            return await job.RunAsync(data, args);
        }

        static IEnumerable<IJob> CreateJobs()
            => typeof(IJob).Assembly.GetTypes()
                .Where(e => typeof(IJob).IsAssignableFrom(e) && e.IsClass && !e.IsAbstract && e.GetConstructor(Type.EmptyTypes) != null)
                .Select(e => (IJob)Activator.CreateInstance(e)!);
    }
}
