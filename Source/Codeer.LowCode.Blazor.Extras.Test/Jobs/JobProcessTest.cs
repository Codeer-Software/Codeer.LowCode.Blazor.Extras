using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// 親側 (JobProcess) ↔ 子側 (JobMode) をプロセス越しに。子はテスト用の最小ホスト Codeer.LowCode.Blazor.Extras.Test.JobHost
    /// (Echo = 受け取った内容を最後の行に返す / Fail = 例外 / Silent = null)。dotnet &lt;dll&gt; と apphost の両方の起動経路を通す。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobProcessTest
    {
        string _entryDll = null!;
        string _hostExecutable = null!;
        string _dotnetPath = null!;
        ILoggerFactory? _loggerFactory;
        List<string> _logs = null!;

        [SetUp]
        public void SetUp()
        {
            _entryDll = JobProcess.EntryDll;
            _hostExecutable = JobProcess.HostExecutable;
            _dotnetPath = JobProcess.DotnetPath;
            _loggerFactory = JobProcess.LoggerFactory;
            _logs = [];
            JobProcess.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs)));
            JobProcess.EntryDll = HostDll;
            JobProcess.HostExecutable = string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            JobProcess.LoggerFactory?.Dispose();
            JobProcess.EntryDll = _entryDll;
            JobProcess.HostExecutable = _hostExecutable;
            JobProcess.DotnetPath = _dotnetPath;
            JobProcess.LoggerFactory = _loggerFactory;
        }

        //テスト用ホスト (Test.csproj の ProjectReference でこのテストの bin に写る。依存はこのテストと同じ)
        static string HostDll
        {
            get
            {
                var dll = Path.Combine(Path.GetDirectoryName(typeof(JobProcessTest).Assembly.Location)!, "Codeer.LowCode.Blazor.Extras.Test.JobHost.dll");
                Assert.That(File.Exists(dll), Is.True, dll);
                return dll;
            }
        }

        [Test]
        public async Task 同期_日本語の入力が標準入力で往復しユーザーIdが環境変数で渡り最後の行が文言になる()
        {
            var input = "{\"data\":{\"氏名\":\"山田 太郎\"},\"submit\":null}";
            var result = await JobProcess.RunAsync("Echo", input, "user-7");
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo($"user-7|1|{input}"));
            Assert.That(result.Output, Does.Contain("progress 1").And.Contain("progress 2"));
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 同期_ジョブの例外は終了コード1で例外のメッセージが文言になり全文がログに出る()
        {
            var result = await JobProcess.RunAsync("Fail", "{}", "u");
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(JobMode.Failure));
            Assert.That(result.ErrorMessage, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(result.Output, Does.Contain("before failure").And.Contain("InvalidOperationException"));
            Assert.That(_logs, Has.Count.EqualTo(1));
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)").And.Contain("before failure"));
        }

        [Test]
        public async Task 同期_無いジョブは終了コード2で子の標準エラーが文言になる()
        {
            var result = await JobProcess.RunAsync("Nope", "{}", "u");
            Assert.That(result.ExitCode, Is.EqualTo(JobMode.JobNotFound));
            Assert.That(result.ErrorMessage, Is.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_logs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 同期_戻り値がnullなら文言は空()
        {
            var result = await JobProcess.RunAsync("Silent", "{}", "u");
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task 同期_apphostで起動しても同じ()
        {
            var appHost = Path.ChangeExtension(HostDll, OperatingSystem.IsWindows() ? ".exe" : null);
            Assume.That(File.Exists(appHost), Is.True, appHost);
            JobProcess.HostExecutable = appHost;
            var result = await JobProcess.RunAsync("Echo", "入力", "u");
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("u|1|入力"));
        }

        [Test]
        public async Task 非同期_起動したら返り失敗は裏でログに出る()
        {
            var started = DateTime.UtcNow;
            JobProcess.Start("Fail", "{}", "u");
            Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(2)), "待たずに返る");
            await WaitAsync(() => _logs.Count == 1);
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)"));
        }

        [Test]
        public async Task 同期_ジョブが改行で終わらない出力を残しても文言は混ざらない()
        {
            var result = await JobProcess.RunAsync("Partial", "{}", "u");
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("done"));
            Assert.That(result.Output, Does.Contain("processing..."));
        }

        [Test]
        public void 起動できなければ例外()
        {
            JobProcess.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.That(() => JobProcess.Start("Echo", "{}", "u"), Throws.Exception);
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobProcess.RunAsync("Echo", "{}", "u"));
        }

        static async Task WaitAsync(Func<bool> condition)
        {
            for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(100);
            Assert.That(condition(), Is.True, "timeout");
        }

        class ListLoggerProvider(List<string> logs) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new ListLogger(logs);
            public void Dispose() { }

            class ListLogger(List<string> logs) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (logs) logs.Add(formatter(state, exception));
                }
            }
        }
    }
}
