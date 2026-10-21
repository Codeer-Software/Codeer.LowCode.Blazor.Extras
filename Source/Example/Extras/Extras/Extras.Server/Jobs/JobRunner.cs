using Codeer.LowCode.Blazor.DbAccess;
using Extras.Server.AI;
using Extras.Server.Services;

namespace Extras.Server.Jobs
{
    /// <summary>
    /// ジョブ (バッチ) モード。Program.cs が dotnet Extras.Server.dll job &lt;ジョブ名&gt; [引数...] のときに呼ぶ。
    /// Web ホストは立てずにジョブを 1 つ実行する。戻り値はプロセスの終了コード (成功 0 / 失敗 1 / ジョブが無い 2)。
    /// 引数の --stdin は標準入力の内容 (JSON) に置き換える (JobProcess が画面の内容をこれで渡す。手で動かすときは echo {...} | dotnet Extras.Server.dll job Sample --stdin)。
    /// 成功したときは最後にジョブの戻り値を 1 行 (null なら空行) 標準出力に書く (画面に出る文言)。
    /// 失敗したときは標準エラーに例外のメッセージだけ (画面に出る文言)、例外の全文は標準出力に出す。
    /// </summary>
    public static class JobRunner
    {
        public const int Success = 0;
        public const int Failure = 1;
        public const int JobNotFound = 2;

        public static async Task<int> RunAsync(IConfiguration config, string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            if (args.Length == 0)
            {
                Console.WriteLine("使い方: Extras.Server job <ジョブ名> [引数...]");
                Console.WriteLine($"ジョブ: {string.Join(", ", GetJobNames())}");
                return JobNotFound;
            }

            var job = FindJob(args[0]);
            if (job == null)
            {
                Console.Error.WriteLine($"ジョブ '{args[0]}' がありません");
                Console.WriteLine($"ジョブ: {string.Join(", ", GetJobNames())}");
                return JobNotFound;
            }

            string? message;
            try
            {
                using (var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true)))
                {
                    //意味検索の警告ログ (埋め込みの失敗など) を標準出力に出す
                    SemanticSearchIndex.Service.LoggerFactory = loggerFactory;
                    //実行した SQL とパラメータを標準出力に出す
                    if (config.GetSection("SqlLog").Get<bool>()) DbAccessor.SqlLog = Console.WriteLine;

                    if (!File.Exists(Path.Combine(SystemConfig.Instance.DesignFileDirectory, "App.zip")))
                    {
                        throw new FileNotFoundException($"DesignFileDirectory に App.zip がありません: {SystemConfig.Instance.DesignFileDirectory}");
                    }

                    //権限の条件はこのユーザーで判定される。空なら匿名 (ログインが要るデザインでは読み書きできない)
                    var userId = config["Jobs:UserId"] ?? string.Empty;
                    await using var data = new DataService(userId);
                    message = await job.RunAsync(data, ReadStandardInput(args[1..]));
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.WriteLine(e);
                return Failure;
            }

            //ロガーを閉じて出し切ってから書く (標準出力の最後の行が画面の成功の通知になる。null なら空行 = 既定の文言)
            Console.WriteLine(message?.ReplaceLineEndings(" ") ?? string.Empty);
            return Success;
        }

        //--stdin を標準入力の内容に置き換える (UTF-8。JobProcess が書いて閉じる)
        static string[] ReadStandardInput(string[] args)
        {
            if (!args.Contains("--stdin")) return args;
            using var reader = new StreamReader(Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));
            var input = reader.ReadToEnd();
            return args.Select(e => e == "--stdin" ? input : e).ToArray();
        }

        public static IReadOnlyList<string> GetJobNames()
            => CreateJobs().Select(e => e.Name).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();

        static IJob? FindJob(string name)
            => CreateJobs().FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

        static IEnumerable<IJob> CreateJobs()
            => typeof(IJob).Assembly.GetTypes()
                .Where(e => typeof(IJob).IsAssignableFrom(e) && e.IsClass && !e.IsAbstract && e.GetConstructor(Type.EmptyTypes) != null)
                .Select(e => (IJob)Activator.CreateInstance(e)!);
    }
}
