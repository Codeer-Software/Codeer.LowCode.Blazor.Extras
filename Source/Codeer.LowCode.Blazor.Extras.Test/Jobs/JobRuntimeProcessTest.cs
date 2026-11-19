using Codeer.LowCode.Blazor.Extras.JobTrigger;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// 親側 (JobRuntime.RunInNewProcessAsync・JobTriggerTracker) ↔ 子側 (JobRuntime.RunFromCommandLineAsync) をプロセス越しに。子はテスト用の最小ホスト Codeer.LowCode.Blazor.Extras.Test.JobHost
    /// (Echo = 受け取った内容を最後の行に返す / Fail = 例外 / Silent = null / Partial = 改行で終わらない出力 / Sleep = 受け取ったミリ秒だけ動く)。dotnet &lt;dll&gt; と apphost の両方の起動経路を通す。
    /// 非同期の状態の置き場 (JobTriggerTracker.Cache) は時計を差し替えた MemoryDistributedCache。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobRuntimeProcessTest
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
            _entryDll = JobRuntime.EntryDll;
            _hostExecutable = JobRuntime.HostExecutable;
            _dotnetPath = JobRuntime.DotnetPath;
            _loggerFactory = JobTriggerTracker.LoggerFactory;
            _finishedRetention = JobTriggerTracker.FinishedRetention;
            _logs = [];
            JobTriggerTracker.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs)));
            JobRuntime.EntryDll = HostDll;
            JobRuntime.HostExecutable = string.Empty;
            _clock = new ManualClock();
#pragma warning disable CS0618
            JobTriggerTracker.Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions { Clock = _clock }));
#pragma warning restore CS0618
        }

        [TearDown]
        public void TearDown()
        {
            JobTriggerTracker.LoggerFactory?.Dispose();
            JobRuntime.EntryDll = _entryDll;
            JobRuntime.HostExecutable = _hostExecutable;
            JobRuntime.DotnetPath = _dotnetPath;
            JobTriggerTracker.LoggerFactory = _loggerFactory;
            JobTriggerTracker.FinishedRetention = _finishedRetention;
            JobTriggerTracker.Cache = null;
        }

        //テスト用ホスト (Test.csproj の ProjectReference でこのテストの bin に写る。依存はこのテストと同じ)
        static string HostDll
        {
            get
            {
                var dll = Path.Combine(Path.GetDirectoryName(typeof(JobRuntimeProcessTest).Assembly.Location)!, "Codeer.LowCode.Blazor.Extras.Test.JobHost.dll");
                Assert.That(File.Exists(dll), Is.True, dll);
                return dll;
            }
        }

        [Test]
        public async Task 同期_日本語の入力が標準入力で往復しユーザーIdが環境変数で渡り最後の行が文言になる()
        {
            var input = "{\"data\":{\"氏名\":\"山田 太郎\"},\"submit\":null}";
            var result = (await JobTriggerTracker.RunAsync("Echo", input, "user-7"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo($"user-7|1|{input}"));
            Assert.That(result.Output, Does.Contain("progress 1").And.Contain("progress 2"));
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 同期_ジョブの例外は終了コード1で例外のメッセージが文言になり全文がログに出る()
        {
            var result = (await JobTriggerTracker.RunAsync("Fail", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ExitCode, Is.EqualTo(JobRuntime.ExitCodeFailure));
            Assert.That(result.ErrorMessage, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(result.Output, Does.Contain("before failure").And.Contain("InvalidOperationException"));
            Assert.That(_logs, Has.Count.EqualTo(1));
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)").And.Contain("before failure"));
        }

        [Test]
        public async Task 同期_無いジョブは終了コード2で子の標準エラーが文言になる()
        {
            var result = (await JobTriggerTracker.RunAsync("Nope", "{}", "u"))!;
            Assert.That(result.ExitCode, Is.EqualTo(JobRuntime.ExitCodeJobNotFound));
            Assert.That(result.ErrorMessage, Is.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_logs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 同期_戻り値がnullなら文言は空()
        {
            var result = (await JobTriggerTracker.RunAsync("Silent", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task 同期_apphostで起動しても同じ()
        {
            var appHost = Path.ChangeExtension(HostDll, OperatingSystem.IsWindows() ? ".exe" : null);
            Assume.That(File.Exists(appHost), Is.True, appHost);
            JobRuntime.HostExecutable = appHost;
            var result = (await JobTriggerTracker.RunAsync("Echo", "入力", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("u|1|入力"));
        }

        [Test]
        public async Task 非同期_起動したらIdが返り失敗はerrorとメッセージになり裏でログにも出る()
        {
            var started = DateTime.UtcNow;
            var jobId = (await JobTriggerTracker.StartAsync("Fail", "{}", "u"))!;
            Assert.That(DateTime.UtcNow - started, Is.LessThan(TimeSpan.FromSeconds(2)), "待たずに返る");
            Assert.That(jobId, Is.Not.Empty);
            Assert.That((await JobTriggerTracker.GetStatusAsync(jobId, "u"))!.Status, Is.EqualTo(JobTriggerStatus.Running));

            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Error));
            Assert.That(status.Error, Is.EqualTo("わざと失敗 (日本語)"));
            await WaitAsync(() => _logs.Count == 1);
            Assert.That(_logs[0], Does.StartWith("ジョブ Fail が失敗しました (終了コード 1): わざと失敗 (日本語)"));
        }

        [Test]
        public async Task 非同期_成功はdoneとMessage()
        {
            var jobId = (await JobTriggerTracker.StartAsync("Echo", "入力", "u"))!;

            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That(status.Message, Is.EqualTo("u|1|入力"));
            Assert.That(status.Error, Is.Empty);
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 同期_返したデータはResultJsonで文言は最後の行()
        {
            var result = (await JobTriggerTracker.RunAsync("Result", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo(JobRuntime.ResultJsonPrefix + "作りました"));
            Assert.That(result.ResultJson, Is.EqualTo("{\"Total\":12800,\"Name\":\"山田\"}"));
        }

        [Test]
        public async Task 非同期_返したデータは状態のResultJsonで返る()
        {
            var jobId = (await JobTriggerTracker.StartAsync("Result", "{}", "u"))!;
            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That(status.ResultJson, Is.EqualTo("{\"Total\":12800,\"Name\":\"山田\"}"));
            Assert.That(status.Result!["Total"], Is.EqualTo(12800m));

            //返さないジョブ・失敗したジョブは null
            Assert.That((await WaitFinishedAsync((await JobTriggerTracker.StartAsync("Echo", "{}", "u"))!, "u")).ResultJson, Is.Null);
            var failed = await WaitFinishedAsync((await JobTriggerTracker.StartAsync("Fail", "{}", "u"))!, "u");
            Assert.That(failed.Status, Is.EqualTo(JobTriggerStatus.Error));
            Assert.That(failed.ResultJson, Is.Null);
            Assert.That(failed.Result, Is.Null);
        }

        [Test]
        public async Task 非同期_知らないIdと別ユーザーはnull()
        {
            var jobId = (await JobTriggerTracker.StartAsync("Echo", "{}", "u"))!;

            Assert.That(await JobTriggerTracker.GetStatusAsync(jobId, "other"), Is.Null);
            Assert.That(await JobTriggerTracker.GetStatusAsync(Guid.NewGuid().ToString("N"), "u"), Is.Null);
            Assert.That(await JobTriggerTracker.GetStatusAsync(string.Empty, "u"), Is.Null);
            await WaitFinishedAsync(jobId, "u");
            Assert.That(await JobTriggerTracker.GetStatusAsync(jobId, "other"), Is.Null);
        }

        [Test]
        public async Task 非同期_FinishedRetentionを過ぎた結果は消え実行中は消えない()
        {
            JobTriggerTracker.FinishedRetention = TimeSpan.FromMinutes(1);
            var jobId = (await JobTriggerTracker.StartAsync("Echo", "{}", "u"))!;
            Assert.That((await WaitFinishedAsync(jobId, "u")).Status, Is.EqualTo(JobTriggerStatus.Done));
            var running = (await JobTriggerTracker.StartAsync("Sleep", "3000", "u"))!;

            _clock.UtcNow += TimeSpan.FromSeconds(59);
            Assert.That(await JobTriggerTracker.GetStatusAsync(jobId, "u"), Is.Not.Null);
            _clock.UtcNow += TimeSpan.FromSeconds(2);
            Assert.That(await JobTriggerTracker.GetStatusAsync(jobId, "u"), Is.Null);

            //実行中は期限なし
            _clock.UtcNow += TimeSpan.FromDays(1);
            Assert.That((await JobTriggerTracker.GetStatusAsync(running, "u"))!.Status, Is.EqualTo(JobTriggerStatus.Running));
            Assert.That((await WaitFinishedAsync(running, "u")).Status, Is.EqualTo(JobTriggerStatus.Done));
        }

        [Test]
        public async Task 非同期_Cacheが無ければIdは返るが状態は問い合わせられない()
        {
            JobTriggerTracker.Cache = null;
            var jobId = await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            Assert.That(jobId, Is.Not.Null.And.Not.Empty);
            Assert.That(await JobTriggerTracker.GetStatusAsync(jobId!, "u"), Is.Null);

            //終われば同じ名前をまた起動できる (実行中の数は状態の置き場と関係なく減る)
            string? next = null;
            for (var i = 0; i < 300 && next == null; i++)
            {
                await Task.Delay(100);
                next = await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            }
            Assert.That(next, Is.Not.Null);
        }

        [Test]
        public async Task 同期_ジョブが改行で終わらない出力を残しても文言は混ざらない()
        {
            var result = (await JobTriggerTracker.RunAsync("Partial", "{}", "u"))!;
            Assert.That(result.Succeeded, Is.True, result.ToString());
            Assert.That(result.Message, Is.EqualTo("done"));
            Assert.That(result.Output, Does.Contain("processing..."));
        }

        [Test]
        public void 起動できなければ例外()
        {
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.That(async () => await JobTriggerTracker.StartAsync("Echo", "{}", "u"), Throws.Exception);
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobTriggerTracker.RunAsync("Echo", "{}", "u"));
        }

        [Test]
        public async Task 同時実行を許さなければ実行中の同名はnullで終わった後は起動できる()
        {
            var first = await JobTriggerTracker.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(first, Is.Not.Null);

            //起動したユーザーに関わらず同じジョブ名で止まる
            Assert.That(await JobTriggerTracker.StartAsync("Sleep", "10", "other", allowConcurrent: false), Is.Null);

            Assert.That((await WaitFinishedAsync(first!, "u")).Status, Is.EqualTo(JobTriggerStatus.Done));
            var next = await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            Assert.That(next, Is.Not.Null);
            await WaitFinishedAsync(next!, "u");
        }

        [Test]
        public async Task 同時実行を許すなら同名が実行中でも並んで起動できる()
        {
            var first = await JobTriggerTracker.StartAsync("Sleep", "2000", "u");
            var second = await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: true);
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));

            Assert.That((await WaitFinishedAsync(second!, "u")).Status, Is.EqualTo(JobTriggerStatus.Done));
            Assert.That((await WaitFinishedAsync(first!, "u")).Status, Is.EqualTo(JobTriggerStatus.Done));
        }

        [Test]
        public async Task 同期も同時実行を許さなければ実行中の同名ではnullで起動しない()
        {
            //非同期が実行中 → 同期は起動しない
            var running = await JobTriggerTracker.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobTriggerTracker.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            await WaitFinishedAsync(running!, "u");

            //同期が実行中 → 非同期も起動しない
            var sync = JobTriggerTracker.RunAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            var result = await sync;
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Message, Is.EqualTo("slept"));

            //終わったら起動できる
            Assert.That(await JobTriggerTracker.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Not.Null);
        }

        [Test]
        public async Task 名前の大文字小文字が違っても同じジョブとして止める()
        {
            var running = await JobTriggerTracker.StartAsync("Sleep", "3000", "u", allowConcurrent: false);
            Assert.That(await JobTriggerTracker.StartAsync("SLEEP", "10", "u", allowConcurrent: false), Is.Null);
            Assert.That(await JobTriggerTracker.RunAsync("sleep", "10", "u", allowConcurrent: false), Is.Null);
            await WaitFinishedAsync(running!, "u");
        }

        [Test]
        public void 起動に失敗したら実行中に数えない()
        {
            //数えたままなら 2 回目は起動せずに null が返る
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.That(async () => await JobTriggerTracker.StartAsync("Echo", "{}", "u", allowConcurrent: false), Throws.Exception);
            Assert.That(async () => await JobTriggerTracker.StartAsync("Echo", "{}", "u", allowConcurrent: false), Throws.Exception);
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobTriggerTracker.RunAsync("Echo", "{}", "u", allowConcurrent: false));
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobTriggerTracker.RunAsync("Echo", "{}", "u", allowConcurrent: false));
        }

        [Test]
        public async Task RunInNewProcessAsync_成功はジョブの戻り値の文言が返る()
        {
            var result = await JobRuntime.RunInNewProcessAsync("Echo", "入力", "user-7");
            Assert.That(result.Message, Is.EqualTo("user-7|1|入力"));
            Assert.That(result.ResultJson, Is.Null);
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task RunInNewProcessAsync_返したデータはResultJsonで返る()
        {
            var result = await JobRuntime.RunInNewProcessAsync("Result", "{}", "u");
            Assert.That(result.Message, Is.EqualTo(JobRuntime.ResultJsonPrefix + "作りました"));
            Assert.That(result.ResultJson, Is.EqualTo("{\"Total\":12800,\"Name\":\"山田\"}"));
        }

        [Test]
        public async Task RunInNewProcessAsync_ジョブがnullを返したら空の文言()
        {
            Assert.That((await JobRuntime.RunInNewProcessAsync("Silent", "{}", "u")).Message, Is.EqualTo(string.Empty));
        }

        //失敗の記録は呼び出し側 (JobTriggerApi) が行うので、ここでは記録しない
        [Test]
        public void RunInNewProcessAsync_失敗は例外でメッセージが標準エラーの最初の行になり記録しない()
        {
            Assert.That(async () => await JobRuntime.RunInNewProcessAsync("Fail", "{}", "u"),
                Throws.Exception.With.Message.EqualTo("わざと失敗 (日本語)"));
            Assert.That(async () => await JobRuntime.RunInNewProcessAsync("Nope", "{}", "u"),
                Throws.Exception.With.Message.EqualTo("ジョブ 'Nope' がありません"));
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public void RunInNewProcessAsync_起動できなければ起動の例外()
        {
            JobRuntime.DotnetPath = Path.Combine(Path.GetTempPath(), "no_such_dotnet_" + Guid.NewGuid());
            Assert.ThrowsAsync<System.ComponentModel.Win32Exception>(async () => await JobRuntime.RunInNewProcessAsync("Echo", "{}", "u"));
        }

        //実行中として数えるのは呼び出し側 (JobTriggerApi)。動いている間も同時実行を許さない起動は止まらない
        [Test]
        public async Task RunInNewProcessAsync_実行中として数えない()
        {
            var running = JobRuntime.RunInNewProcessAsync("Sleep", "5000", "u");
            Assert.That(await JobTriggerTracker.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Not.Null);
            var started = await JobTriggerTracker.StartAsync("Sleep", "10", "u", allowConcurrent: false);
            Assert.That(started, Is.Not.Null);
            Assert.That(running.IsCompleted, Is.False, "Sleep 5000 が先に終わっていては確かめたことにならない");
            await WaitFinishedAsync(started!, "u");
            Assert.That((await running).Message, Is.EqualTo("slept"));
        }

        //終わるまで状態を問い合わせる (最大 30 秒)
        static async Task<JobTriggerStatusResponse> WaitFinishedAsync(string jobId, string userId)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                var status = await JobTriggerTracker.GetStatusAsync(jobId, userId);
                Assert.That(status, Is.Not.Null, "状態が取れない");
                if (status!.Status != JobTriggerStatus.Running) return status;
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
