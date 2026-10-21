using Codeer.LowCode.Blazor.Extras.Server.Jobs;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>子側 (JobMode) を同一プロセスで。Console を差し替えて終了コードと出力を見る (--stdin の置き換えは標準入力そのものを読むので JobProcessTest でプロセス越しに)。</summary>
    [TestFixture, NonParallelizable]
    public class JobModeTest
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
            var code = await JobMode.RunAsync([], Names, (_, _) => throw new Exception("呼ばれない"));
            Assert.That(code, Is.EqualTo(JobMode.JobNotFound));
            Assert.That(_stdout.ToString(), Does.Contain("job <ジョブ名>").And.Contain("ジョブ: CloseOrders, Sample"));
        }

        [Test]
        public async Task 無いジョブは標準エラーに1行とJobNotFound()
        {
            var code = await JobMode.RunAsync(["Nope"], Names, (_, _) => throw new Exception("呼ばれない"));
            Assert.That(code, Is.EqualTo(JobMode.JobNotFound));
            Assert.That(_stderr.ToString().Trim(), Is.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_stdout.ToString(), Does.Contain("ジョブ: CloseOrders, Sample"));
        }

        [Test]
        public async Task 名前は大文字小文字を区別せず登録した名前で呼ばれ戻り値が最後の行になる()
        {
            string? calledName = null;
            string[]? calledArgs = null;
            var code = await JobMode.RunAsync(["closeorders", "a", "b"], Names, (name, args) =>
            {
                calledName = name; calledArgs = args;
                Console.WriteLine("途中の出力");
                return Task.FromResult<string?>("3 件を締めました\r\n2 行目");
            });
            Assert.That(code, Is.EqualTo(JobMode.Success));
            Assert.That(calledName, Is.EqualTo("CloseOrders"));
            Assert.That(calledArgs, Is.EqualTo(new[] { "a", "b" }));
            //戻り値は改行を潰して最後の 1 行に。JobResult.Message がこれを取り出す
            var result = new JobResult("CloseOrders", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Message, Is.EqualTo("3 件を締めました 2 行目"));
        }

        [Test]
        public async Task 戻り値がnullなら空行で終わりMessageは空()
        {
            var code = await JobMode.RunAsync(["Sample"], Names, (_, _) =>
            {
                Console.WriteLine("途中の出力");
                return Task.FromResult<string?>(null);
            });
            Assert.That(code, Is.EqualTo(JobMode.Success));
            Assert.That(new JobResult("Sample", code, _stdout.ToString(), _stderr.ToString()).Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task 例外は標準エラーにメッセージだけ標準出力に全文でFailure()
        {
            var code = await JobMode.RunAsync(["Sample"], Names, (_, _) => throw new InvalidOperationException("在庫が足りません"));
            Assert.That(code, Is.EqualTo(JobMode.Failure));
            Assert.That(_stderr.ToString().Trim(), Is.EqualTo("在庫が足りません"));
            Assert.That(_stdout.ToString(), Does.Contain("InvalidOperationException").And.Contain("在庫が足りません"));
            var result = new JobResult("Sample", code, _stdout.ToString(), _stderr.ToString());
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("在庫が足りません"));
        }
    }
}
