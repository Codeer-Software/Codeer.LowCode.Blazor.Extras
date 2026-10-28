using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// 親側 (JobProcess) ↔ 子側 (JobMode) をプロセス越しに。子はテスト用の最小ホスト Codeer.LowCode.Blazor.Extras.Test.JobHost
    /// (Echo = 受け取った内容を最後の行に返す / Fail = 例外 / Silent = null / Partial = 改行で終わらない出力 / Sleep = 受け取ったミリ秒だけ動く)。dotnet &lt;dll&gt; と apphost の両方の起動経路を通す。
    /// 非同期の状態の置き場 (JobProcess.Cache) は時計を差し替えた MemoryDistributedCache。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobProcessTest
    {
        string _entryDll = null!;
        string _hostExecutable = null!;
        string _dotnetPath = null!;
        ILoggerFactory? _loggerFactory;
        TimeSpan _finishedRetention;
        List<string> _logs = null!;
        ManualClock _clock = null!;

        [SetUp]
        public void SetUp()
        {
            _entryDll = JobProcess.EntryDll;
            _hostExecutable = JobProcess.HostExecutable;
            _dotnetPath = JobProcess.DotnetPath;
            _loggerFactory = JobProcess.LoggerFactory;
            _finishedRetention = JobProcess.FinishedRetention;
            _logs = [];
            JobProcess.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs)));
            JobProcess.EntryDll = HostDll;
            JobProcess.HostExecutable = string.Empty;
            _clock = new ManualClock();
#pragma warning disable CS0618
            JobProcess.Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { Clock = _clock }));
#pragma warning restore CS0618
        }

        [TearDown]
        public void TearDown()
        {
            JobProcess.LoggerFactory?.Dispose();
            JobProcess.EntryDll = _entryDll;
            JobProcess.HostExecutable = _hostExecutable;
            JobProcess.DotnetPath = _dotnetPath;
            JobProcess.LoggerFactory = _loggerFactory;
            JobProcess.FinishedRetention = _finishedRetention;
            JobProcess.Cache = null;
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
            var result = (await JobProcess.RunAsync("Echo", input, "user-7"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo($"user-7|1|{input}"));
            Assert.That(result.Output, Does.Contain("progress 1").And.Contain("progress 2"));
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 同期_ジョブの例外は終了コード1で例外のメッセージが文言になり全文がログに出る()
        {
            var result = (await JobProcess.RunAsync("Fail", "{}", "u"))!;
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
            var result = (await JobProcess.RunAsync("Nope", "{}", "u"))!;
            Assert.That(result.ExitCode, Is.EqualTo(JobMode.JobNotFound));
            Assert.That(result.ErrorMessage, Is.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_logs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 同期_戻り値がnullなら文言は空()
        {
            var result = (await JobProcess.RunAsync("Silent", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task 同期_apphostで起動しても同じ()
        {
            var appHost = Path.ChangeExtension(HostDll, OperatingSystem.IsWindows() ? ".exe" : null);
            Assume.That(File.Exists(appHost), Is.True, appHost);
            JobProcess.HostExecutable = appHost;
            var result = (await JobProcess.RunAsync("Echo", "入力", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("u|1|入力"));
        }

        [Test]
        public async Task 非同期_起動したらIdが返り失敗はerrorとメッセージになり裏でログにも出る()
        {
            var started = DateTime.UtcNow;
            var jobId = (await JobProcess.StartAsync("Fail", "{}", "u"))!;
            Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(2)), "待たずに返る");
            Assert.That(jobId, Is.Not.Empty);
            Assert.That((await JobProcess.GetStatusAsync(jobId, "u"))!.Status, Is.EqualTo(JobStatus.Running));

            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobStatus.Error));
            Assert.That(status.Error, Is.EqualTo("わざと失敗 (日本語)"));
            await WaitAsync(() => _logs.Count == 1);
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)"));
        }

        [Test]
        public async Task 非同期_成功はdoneとMessage()
        {
            var jobId = (await JobProcess.StartAsync("Echo", "入力", "u"))!;

            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobStatus.Done));
            Assert.That(status.Message, Is.EqualTo("u|1|入力"));
            Assert.That(status.Error, Is.Empty);
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 非同期_知らないIdと別ユーザーはnull()
        {
            var jobId = (await JobProcess.StartAsync("Echo", "{}", "u"))!;

            Assert.That(await JobProcess.GetStatusAsync(jobId, "other"), Is.Null);
            Assert.That(await JobProcess.GetStatusAsync(Guid.NewGuid().ToString("N"), "u"), Is.Null);
            Assert.That(await JobProcess.GetStatusAsync(string.Empty, "u"), Is.Null);
            await WaitFinishedAsync(jobId, "u");
            Assert.That(await JobProcess.GetStatusAsync(jobId, "other"), Is.Null);
        }

        [Test]
        public async Task 非同期_FinishedRetentionを過ぎた結果は消え実行中は消えない()
        {
            JobProcess.FinishedRetention = TimeSpan.FromMinutes(1);
            var jobId = (await JobProcess.StartAsync("Echo", "{}", "u"))!;
            Assert.That((await WaitFinishedAsync(jobId, "u")).Status, Is.EqualTo(JobStatus.Done));
            var running = (await JobProcess.StartAsync("Sleep", "3000", "u"))!;

            _clock.UtcNow += TimeSpan.FromSeconds(59);
            Assert.That(await JobProcess.GetStatusAsync(jobId, "u"), Is.Not.Null);
            _clock.UtcNow += TimeSpan.FromSeconds(2);
            Assert.That(await JobProcess.GetStatusAsync(jobId, "u"), Is.Null);

            //実行中は期限なし
            _clock.UtcNow += TimeSpan.FromDays(1);
            Assert.That((await JobProcess.GetStatusAsync(running, "u"))!.Status, Is.EqualTo(JobStatus.Running));
            Assert.That((await WaitFinishedAsync(running, "u")).Status, Is.EqualTo(JobStatus.Done));
        }

        [Test]
        public async Task 非同期_Cacheが無ければIdは返るが状態は問い合わせられない()
        {
            JobProcess.Cache = null;
            var jobId = await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            Assert.That(jobId, Is.Not.Null.And.Not.Empty);
            Assert.That(await JobProcess.GetStatusAsync(jobId!, "u"), Is.Null);

            //終われば同じ名前をまた起動できる (実行中の数は状態の置き場と関係なく減る)
            string? next = null;
            for (var i = 0; i < 300 && next == null; i++)
            {
                await Task.Delay(100);
                next = await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            }
            Assert.That(next, Is.Not.Null);
        }

        [Test]
        public async Task 同期_ジョブが改行で終わらない出力を残しても文言は混ざらない()
        {
            var result = (await JobProcess.RunAsync("Partial", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("done"));
            Assert.That(result.Output, Does.Contain("processing..."));
        }

        [Test]
        public void 起動できなければ例外()
        {
            JobProcess.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.That(async () => await JobProcess.StartAsync("Echo", "{}", "u"), Throws.Exception);
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobProcess.RunAsync("Echo", "{}", "u"));
        }

        [Test]
        public async Task 同時実行を許さなければ実行中の同名はnullで終わった後は起動できる()
        {
            var first = await JobProcess.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(first, Is.Not.Null);

            //起動したユーザーに関わらず同じジョブ名で止まる
            Assert.That(await JobProcess.StartAsync("Sleep", "10", "other", allowConcurrent: false), Is.Null);

            Assert.That((await WaitFinishedAsync(first!, "u")).Status, Is.EqualTo(JobStatus.Done));
            var next = await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            Assert.That(next, Is.Not.Null);
            await WaitFinishedAsync(next!, "u");
        }

        [Test]
        public async Task 同時実行を許すなら同名が実行中でも並んで起動できる()
        {
            var first = await JobProcess.StartAsync("Sleep", "2000", "u");
            var second = await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: true);
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));

            Assert.That((await WaitFinishedAsync(second!, "u")).Status, Is.EqualTo(JobStatus.Done));
            Assert.That((await WaitFinishedAsync(first!, "u")).Status, Is.EqualTo(JobStatus.Done));
        }

        [Test]
        public async Task 同期も同時実行を許さなければ実行中の同名ではnullで起動しない()
        {
            //非同期が実行中 → 同期は起動しない
            var running = await JobProcess.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobProcess.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            await WaitFinishedAsync(running!, "u");

            //同期が実行中 → 非同期も起動しない
            var sync = JobProcess.RunAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            var result = await sync;
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Message, Is.EqualTo("slept"));

            //終わったら起動できる
            Assert.That(await JobProcess.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Not.Null);
        }

        [Test]
        public async Task 名前の大文字小文字が違っても同じジョブとして止める()
        {
            var running = await JobProcess.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobProcess.StartAsync("SLEEP", "10", "u", allowConcurrent: false), Is.Null);
            Assert.That(await JobProcess.RunAsync("sleep", "10", "u", allowConcurrent: false), Is.Null);
            await WaitFinishedAsync(running!, "u");
        }

        [Test]
        public void 起動に失敗したら実行中に数えない()
        {
            //数えたままなら 2 回目は起動せずに null が返る
            JobProcess.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.That(async () => await JobProcess.StartAsync("Echo", "{}", "u", allowConcurrent: false), Throws.Exception);
            Assert.That(async () => await JobProcess.StartAsync("Echo", "{}", "u", allowConcurrent: false), Throws.Exception);
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobProcess.RunAsync("Echo", "{}", "u", allowConcurrent: false));
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobProcess.RunAsync("Echo", "{}", "u", allowConcurrent: false));
        }

        //終わるまで状態を問い合わせる (最大 30 秒)
        static async Task<JobStatusResponse> WaitFinishedAsync(string jobId, string userId)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                var status = await JobProcess.GetStatusAsync(jobId, userId);
                Assert.That(status, Is.Not.Null, "状態が取れない");
                if (status!.Status != JobStatus.Running) return status;
                if (DateTime.UtcNow > deadline) Assert.Fail("timeout");
                await Task.Delay(50);
            }
        }

        //時計を進められる MemoryDistributedCache の時計 (.NET 8 の MemoryDistributedCacheOptions には TimeProvider が無いので Obsolete の ISystemClock)
#pragma warning disable CS0618
        class ManualClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        }
#pragma warning restore CS0618

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
