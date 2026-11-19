using Codeer.LowCode.Blazor.Extras.Server.Jobs;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>子側 (JobRuntime) を同一プロセスで。Console を差し替えて終了コードと出力を見る (--stdin の置き換えは標準入力そのものを読むので JobRuntimeProcessTest でプロセス越しに)。</summary>
    [TestFixture, NonParallelizable]
    public class JobRuntimeTest
    {
        TextWriter _out = null!;
        TextWriter _err = null!;
        StringWriter _stdout = null!;
        StringWriter _stderr = null!;

        [SetUp]
        public void SetUp()
        {
            _out = Console.Out; _err = Console.Error;
            Console.SetOut(_stdout = new StringWriter());
            Console.SetError(_stderr = new StringWriter());
        }

        [TearDown]
        public void TearDown()
        {
            Console.SetOut(_out); Console.SetError(_err);
        }

        static readonly string[] Names = ["Sample", "CloseOrders"];

        [Test]
        public async Task 引数なしは使い方とジョブの一覧を出してJobNotFound()
        {
            var code = await JobRuntime.RunFromCommandLineAsync([], Names, (_, _) => Task.FromException<string?>(new Exception("呼ばれない")));
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeJobNotFound));
            Assert.That(_stdout.ToString(), Does.Contain("job <ジョブ名>").And.Contain("ジョブ: CloseOrders, Sample"));
        }

        [Test]
        public async Task 無いジョブは標準エラーに1行とJobNotFound()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Nope"], Names, (_, _) => Task.FromException<string?>(new Exception("呼ばれない")));
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeJobNotFound));
            Assert.That(_stderr.ToString().Trim(), Is.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_stdout.ToString(), Does.Contain("ジョブ: CloseOrders, Sample"));
        }

        [Test]
        public async Task 名前は大文字小文字を区別せず登録した名前で呼ばれ戻り値が最後の行になる()
        {
            string? calledName = null;
            string[]? calledArgs = null;
            var code = await JobRuntime.RunFromCommandLineAsync(["closeorders", "a", "b"], Names, (name, args) =>
            {
                calledName = name; calledArgs = args;
                Console.WriteLine("途中の出力");
                return Task.FromResult<string?>("3 件を締めました\r\n2 行目");
            });
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeSuccess));
            Assert.That(calledName, Is.EqualTo("CloseOrders"));
            Assert.That(calledArgs, Is.EqualTo(new[] { "a", "b" }));
            //戻り値は改行を潰して最後の 1 行に。JobRuntimeProcessResult.Message がこれを取り出す
            var result = new JobRuntimeProcessResult("CloseOrders", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Message, Is.EqualTo("3 件を締めました 2 行目"));
        }

        [Test]
        public async Task 戻り値がnullなら空行で終わりMessageは空()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) =>
            {
                Console.WriteLine("途中の出力");
                return Task.FromResult<string?>(null);
            });
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeSuccess));
            Assert.That(new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString()).Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task 返すデータは最後から2行目に印つきで書かれJobRuntimeProcessResultが取り出せる()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) =>
            {
                Console.WriteLine("途中の出力");
                //JSON の改行は空白になって 1 行に収まる。文言が印で始まっても文言のまま
                return Task.FromResult(new JobRuntimeResult { Message = JobRuntime.ResultJsonPrefix + "作りました", ResultJson = "{\"Total\":12800,\r\n\"Name\":\"山田\"}" });
            });
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeSuccess));
            var nl = Environment.NewLine;
            Assert.That(_stdout.ToString(), Is.EqualTo($"途中の出力{nl}{nl}{JobRuntime.ResultJsonPrefix}{{\"Total\":12800, \"Name\":\"山田\"}}{nl}{JobRuntime.ResultJsonPrefix}作りました{nl}"));
            var result = new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Message, Is.EqualTo(JobRuntime.ResultJsonPrefix + "作りました"));
            Assert.That(result.ResultJson, Is.EqualTo("{\"Total\":12800, \"Name\":\"山田\"}"));
        }

        [Test]
        public async Task 返すデータが無ければ出力は今までと同じでResultJsonはnull()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) =>
            {
                //ジョブ自身が印つきの行を出力しても、データとしては読まない
                Console.WriteLine(JobRuntime.ResultJsonPrefix + "{\"Fake\":1}");
                return Task.FromResult(new JobRuntimeResult { Message = JobRuntime.ResultJsonPrefix + "終わりました" });
            });
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeSuccess));
            var nl = Environment.NewLine;
            Assert.That(_stdout.ToString(), Is.EqualTo($"{JobRuntime.ResultJsonPrefix}{{\"Fake\":1}}{nl}{nl}{JobRuntime.ResultJsonPrefix}終わりました{nl}"));
            var result = new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Message, Is.EqualTo(JobRuntime.ResultJsonPrefix + "終わりました"));
            Assert.That(result.ResultJson, Is.Null);
        }

        [Test]
        public async Task 文言だけ返す形の出力は今までと同じ()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) => Task.FromResult<string?>("完了"));
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeSuccess));
            var nl = Environment.NewLine;
            Assert.That(_stdout.ToString(), Is.EqualTo($"{nl}完了{nl}"));
            Assert.That(new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString()).ResultJson, Is.Null);
        }

        [Test]
        public async Task 失敗したら返すデータは書かない()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) => Task.FromException<JobRuntimeResult>(new InvalidOperationException("在庫が足りません")));
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeFailure));
            Assert.That(_stdout.ToString(), Does.Not.Contain(JobRuntime.ResultJsonPrefix));
            Assert.That(new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString()).ResultJson, Is.Null);
        }

        [Test]
        public async Task 例外は標準エラーにメッセージだけ標準出力に全文でFailure()
        {
            var code = await JobRuntime.RunFromCommandLineAsync(["Sample"], Names, (_, _) => Task.FromException<string?>(new InvalidOperationException("在庫が足りません")));
            Assert.That(code, Is.EqualTo(JobRuntime.ExitCodeFailure));
            Assert.That(_stderr.ToString().Trim(), Is.EqualTo("在庫が足りません"));
            Assert.That(_stdout.ToString(), Does.Contain("InvalidOperationException").And.Contain("在庫が足りません"));
            var result = new JobRuntimeProcessResult("Sample", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("在庫が足りません"));
        }
    }
}
