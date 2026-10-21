using System.Reflection;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブ (バッチ) モードの子側。ホストの Program.cs が <c>dotnet &lt;ホスト&gt;.dll job &lt;ジョブ名&gt; [引数...]</c> のときに、
    /// Web ホストを立てずに <c>job</c> より後ろの引数を渡して呼ぶ。ジョブを 1 つ実行してプロセスの終了コードを返す。
    /// <para>
    /// 親側 (<see cref="JobProcess"/>) との約束: 引数の <c>--stdin</c> は標準入力の内容 (UTF-8) に置き換える。
    /// 標準出力・標準エラーは UTF-8。成功したときは最後にジョブの戻り値を 1 行 (null なら空行) 標準出力に書く (画面に出る文言)。
    /// 失敗したときは標準エラーに例外のメッセージだけ (画面に出る文言)、例外の全文は標準出力に出す。
    /// 手で動かすときは <c>echo {"data":{},"submit":null} | dotnet &lt;ホスト&gt;.dll job Sample --stdin</c>。
    /// </para>
    /// </summary>
    public static class JobMode
    {
        /// <summary>終了コード: 成功。</summary>
        public const int Success = 0;
        /// <summary>終了コード: 失敗 (ジョブが例外を投げた)。</summary>
        public const int Failure = 1;
        /// <summary>終了コード: ジョブ名のジョブが無い (引数なしで使い方を出したときも)。</summary>
        public const int JobNotFound = 2;

        /// <summary>引数のこの値を標準入力の内容に置き換える。</summary>
        public const string StandardInputArgument = "--stdin";

        /// <summary>
        /// ジョブを 1 つ実行する。
        /// </summary>
        /// <param name="args"><c>job</c> より後ろの引数 (ジョブ名・ジョブの引数)。</param>
        /// <param name="jobNames">ホストが持つジョブの名前 (大文字小文字は区別しない)。</param>
        /// <param name="runJob">ジョブを動かす (ジョブ名・ジョブの引数 → 成功の文言。失敗は例外)。ホストがここで DB アクセスとジョブを組み立てる。</param>
        /// <returns>プロセスの終了コード。</returns>
        public static async Task<int> RunAsync(string[] args, IEnumerable<string> jobNames, Func<string, string[], Task<string?>> runJob)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var names = jobNames.OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
            if (args.Length == 0)
            {
                Console.WriteLine($"使い方: {HostName} job <ジョブ名> [引数...]");
                Console.WriteLine($"ジョブ: {string.Join(", ", names)}");
                return JobNotFound;
            }

            var name = names.FirstOrDefault(e => string.Equals(e, args[0], StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                Console.Error.WriteLine($"ジョブ '{args[0]}' がありません");
                Console.WriteLine($"ジョブ: {string.Join(", ", names)}");
                return JobNotFound;
            }

            if (args.Contains(StandardInputArgument) && !Console.IsInputRedirected)
            {
                //端末から --stdin を打つと標準入力の終わりを待ち続ける。間違いだと分かるようにする
                Console.Error.WriteLine($"{StandardInputArgument} には標準入力が要ります (例: echo {{}} | {HostName} job {name} {StandardInputArgument})");
                return Failure;
            }

            string? message;
            try
            {
                message = await runJob(name, ReadStandardInput(args[1..]));
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message);
                Console.WriteLine(e);
                return Failure;
            }

            //ジョブの出力の後に、行を改めてから書く (ジョブが改行で終わらない出力を残していても混ざらない)。標準出力の最後の行が画面の成功の通知になる (null なら空行 = 既定の文言)
            Console.WriteLine();
            Console.WriteLine(message?.ReplaceLineEndings(" ") ?? string.Empty);
            return Success;
        }

        static string HostName
            => Path.GetFileNameWithoutExtension(Assembly.GetEntryAssembly()?.Location ?? Environment.ProcessPath ?? string.Empty);

        //--stdin を標準入力の内容に置き換える (UTF-8。JobProcess が書いて閉じる)
        static string[] ReadStandardInput(string[] args)
        {
            if (!args.Contains(StandardInputArgument)) return args;
            using var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            var input = reader.ReadToEnd();
            return args.Select(e => e == StandardInputArgument ? input : e).ToArray();
        }
    }
}
