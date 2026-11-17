using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// JobApi がホストの実行の口を包んで動かす経路 (JobProcess.RunTrackedAsync / StartTrackedAsync)。実行の口はテストの中のデリゲートで、終わる時を TaskCompletionSource で決める。
    /// 実行中の数・状態の置き場 (Cache)・失敗の記録 (LoggerFactory) は公開の RunAsync / StartAsync と共通の static なので、JobProcessTest と同じく直列で動かし後始末で戻す。
    /// 公開の RunAsync / StartAsync との数え方の共通は、テスト用ホスト (Codeer.LowCode.Blazor.Extras.Test.JobHost) の Sleep で確かめる。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobTrackedRunTest
    {
        string _entryDll = null!;
        string _hostExecutable = null!;
        ILoggerFactory? _loggerFactory;
        List<string> _logs = null!;
        List<LogLevel> _levels = null!;

        [SetUp]
        public void SetUp()
        {
            _entryDll = JobProcess.EntryDll;
            _hostExecutable = JobProcess.HostExecutable;
            _loggerFactory = JobProcess.LoggerFactory;
            _logs = [];
            _levels = [];
            JobProcess.LoggerFactory = LoggerFactory.Create(builder => builder.AddProvider(new ListLoggerProvider(_logs, _levels)));
            JobProcess.Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        }

        [TearDown]
        public void TearDown()
        {
            JobProcess.LoggerFactory?.Dispose();
            JobProcess.EntryDll = _entryDll;
            JobProcess.HostExecutable = _hostExecutable;
            JobProcess.LoggerFactory = _loggerFactory;
            JobProcess.Cache = null;
        }

        [Test]
        public async Task 同期_成功は文言の改行を空白にして返る()
        {
            var outcome = (await JobProcess.RunTrackedAsync("Tracked", () => Task.FromResult<string?>("1 件\r\n処理しました\n"), allowConcurrent: false))!;
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(outcome.Message, Is.EqualTo("1 件 処理しました"));
            Assert.That(outcome.ErrorMessage, Is.Empty);
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 同期_戻り値がnullなら文言は空()
        {
            var outcome = (await JobProcess.RunTrackedAsync("Tracked", () => Task.FromResult<string?>(null), allowConcurrent: false))!;
            Assert.That(outcome.Succeeded, Is.True);
            Assert.That(outcome.Message, Is.Empty);
        }

        [Test]
        public async Task 同期_例外は例外のメッセージが文言になり全文がログに1件出る()
        {
            var outcome = (await JobProcess.RunTrackedAsync("Tracked", async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("わざと失敗 (日本語)");
            }, allowConcurrent: false))!;
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.Message, Is.Empty);
            Assert.That(outcome.ErrorMessage, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(_logs, Has.Count.EqualTo(1));
            Assert.That(_levels, Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(_logs[0], Does.StartWith("ジョブ Tracked が失敗しました: ").And.Contain("InvalidOperationException").And.Contain("わざと失敗 (日本語)"));
        }

        [Test]
        public async Task 同期_Taskを返す前に投げても失敗の結果になる()
        {
            var outcome = (await JobProcess.RunTrackedAsync("Tracked", () => throw new InvalidOperationException("同期で失敗"), allowConcurrent: false))!;
            Assert.That(outcome.Succeeded, Is.False);
            Assert.That(outcome.ErrorMessage, Is.EqualTo("同期で失敗"));
            Assert.That(_logs, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task 同期_同時実行を許さなければ実行中の同名はnullで終わった後はまた動く()
        {
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = JobProcess.RunTrackedAsync("Tracked", () => gate.Task, allowConcurrent: false);
            Assert.That(first.IsCompleted, Is.False);

            //実行中: 同期も非同期も (大文字小文字が違っても) 動かさない。デリゲートは呼ばれない
            var called = 0;
            Func<Task<string?>> second = () => { called++; return Task.FromResult<string?>("second"); };
            Assert.That(await JobProcess.RunTrackedAsync("Tracked", second, allowConcurrent: false), Is.Null);
            Assert.That(await JobProcess.StartTrackedAsync("TRACKED", second, "u", allowConcurrent: false), Is.Null);
            Assert.That(called, Is.EqualTo(0));

            gate.SetResult("first");
            Assert.That((await first)!.Message, Is.EqualTo("first"));

            var next = await JobProcess.RunTrackedAsync("Tracked", second, allowConcurrent: false);
            Assert.That(next!.Message, Is.EqualTo("second"));
            Assert.That(called, Is.EqualTo(1));
        }

        [Test]
        public async Task 同時実行を許すなら実行中の同名でも動く()
        {
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = JobProcess.RunTrackedAsync("Tracked", () => gate.Task, allowConcurrent: true);
            var second = await JobProcess.RunTrackedAsync("Tracked", () => Task.FromResult<string?>("second"), allowConcurrent: true);
            Assert.That(second!.Message, Is.EqualTo("second"));
            gate.SetResult("first");
            Assert.That((await first)!.Message, Is.EqualTo("first"));
        }

        [Test]
        public async Task 例外で終わっても実行中に数えたままにしない()
        {
            Assert.That((await JobProcess.RunTrackedAsync("Tracked", () => throw new InvalidOperationException("x"), allowConcurrent: false))!.Succeeded, Is.False);
            Assert.That((await JobProcess.RunTrackedAsync("Tracked", () => Task.FromResult<string?>("ok"), allowConcurrent: false))!.Succeeded, Is.True);

            var jobId = (await JobProcess.StartTrackedAsync("Tracked", () => throw new InvalidOperationException("y"), "u", allowConcurrent: false))!;
            Assert.That((await WaitFinishedAsync(jobId, "u")).Status, Is.EqualTo(JobStatus.Error));
            await WaitAsync(async () => await JobProcess.RunTrackedAsync("Tracked", () => Task.FromResult<string?>("ok"), allowConcurrent: false) != null);
        }

        [Test]
        public async Task 公開のRunAsyncやStartAsyncと実行の口は同じジョブ名で実行中を数える()
        {
            JobProcess.EntryDll = HostDll;
            JobProcess.HostExecutable = string.Empty;

            //公開の StartAsync (別プロセス) が実行中 → 実行の口は動かさない
            var process = (await JobProcess.StartAsync("Sleep", "3000", "u", allowConcurrent: false))!;
            Assert.That(await JobProcess.RunTrackedAsync("sleep", () => Task.FromResult<string?>("tracked"), allowConcurrent: false), Is.Null);
            Assert.That(await JobProcess.StartTrackedAsync("Sleep", () => Task.FromResult<string?>("tracked"), "u", allowConcurrent: false), Is.Null);
            Assert.That((await WaitFinishedAsync(process, "u")).Status, Is.EqualTo(JobStatus.Done));

            //実行の口が実行中 → 公開の RunAsync / StartAsync も起動しない
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var tracked = JobProcess.RunTrackedAsync("Sleep", () => gate.Task, allowConcurrent: false);
            Assert.That(await JobProcess.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            Assert.That(await JobProcess.StartAsync("Sleep", "10", "u", allowConcurrent: false), Is.Null);
            gate.SetResult("done");
            Assert.That((await tracked)!.Message, Is.EqualTo("done"));

            Assert.That(await JobProcess.RunAsync("Sleep", "10", "u", allowConcurrent: false), Is.Not.Null);
        }

        [Test]
        public async Task 非同期_終わる前にIdが返りRunningからDoneになる()
        {
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobId = await JobProcess.StartTrackedAsync("Tracked", () => gate.Task, "u", allowConcurrent: false);
            Assert.That(jobId, Is.Not.Null.And.Not.Empty);
            Assert.That((await JobProcess.GetStatusAsync(jobId!, "u"))!.Status, Is.EqualTo(JobStatus.Running));

            gate.SetResult("3 件\n締めました");
            var status = await WaitFinishedAsync(jobId!, "u");
            Assert.That(status.Status, Is.EqualTo(JobStatus.Done));
            Assert.That(status.Message, Is.EqualTo("3 件 締めました"));
            Assert.That(status.Error, Is.Empty);
            Assert.That(_logs, Is.Empty);
        }

        [Test]
        public async Task 非同期_例外はErrorと例外のメッセージになりログに1件出る()
        {
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobId = (await JobProcess.StartTrackedAsync("Tracked", () => gate.Task, "u", allowConcurrent: false))!;
            Assert.That((await JobProcess.GetStatusAsync(jobId, "u"))!.Status, Is.EqualTo(JobStatus.Running));

            gate.SetException(new InvalidOperationException("わざと失敗 (日本語)"));
            var status = await WaitFinishedAsync(jobId, "u");
            Assert.That(status.Status, Is.EqualTo(JobStatus.Error));
            Assert.That(status.Error, Is.EqualTo("わざと失敗 (日本語)"));
            Assert.That(status.Message, Is.Empty);
            await WaitAsync(() => Task.FromResult(_logs.Count == 1));
            Assert.That(_levels, Is.EqualTo(new[] { LogLevel.Error }));
            Assert.That(_logs[0], Does.StartWith("ジョブ Tracked が失敗しました: "));
        }

        [Test]
        public async Task 非同期_Taskを返す前に投げても呼び出しは例外にならず状態がErrorになる()
        {
            var jobId = await JobProcess.StartTrackedAsync("Tracked", () => throw new InvalidOperationException("同期で失敗"), "u", allowConcurrent: false);
            Assert.That(jobId, Is.Not.Null.And.Not.Empty);
            var status = await WaitFinishedAsync(jobId!, "u");
            Assert.That(status.Status, Is.EqualTo(JobStatus.Error));
            Assert.That(status.Error, Is.EqualTo("同期で失敗"));
            await WaitAsync(() => Task.FromResult(_logs.Count == 1));
        }

        [Test]
        public async Task 非同期_別のユーザーと知らないIdはnull()
        {
            var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var jobId = (await JobProcess.StartTrackedAsync("Tracked", () => gate.Task, "u", allowConcurrent: true))!;
            Assert.That(await JobProcess.GetStatusAsync(jobId, "other"), Is.Null);
            Assert.That(await JobProcess.GetStatusAsync(Guid.NewGuid().ToString("N"), "u"), Is.Null);

            gate.SetResult("ok");
            await WaitFinishedAsync(jobId, "u");
            Assert.That(await JobProcess.GetStatusAsync(jobId, "other"), Is.Null);
        }

        //要求の監査レコード (AsyncLocal) を、要求が終わった後も動く非同期のジョブに引き継がない (AIChatService・SemanticSearchService と同じ)。
        //引き継ぐと、ジョブの保存が AuditIOInterceptor から書き終えた (または書いている最中の) 要求の監査レコードに足される
        [Test]
        public async Task 非同期_要求の監査レコードをジョブに引き継がない()
        {
            var seen = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            AuditContext.Current = new AuditContext();
            try
            {
                var jobId = await JobProcess.StartTrackedAsync("Tracked", () =>
                {
                    seen.SetResult(AuditContext.Current != null);
                    return Task.FromResult<string?>(null);
                }, "u", allowConcurrent: true);
                Assert.That(jobId, Is.Not.Null);
            }
            finally
            {
                AuditContext.Current = null;
            }
            Assert.That(await seen.Task, Is.False, "ジョブの中で要求の AuditContext.Current が見えている");
        }

        static readonly AsyncLocal<string?> _requestValue = new();

        //同期でも要求の中の AsyncLocal (監査レコード等) はジョブに見えない (ジョブの保存を JobApi の呼び出しの記録に混ぜない。別プロセスと同じ)
        [Test]
        public async Task 同期_要求のAsyncLocalをジョブに引き継がない()
        {
            AuditContext.Current = new AuditContext();
            _requestValue.Value = "request";
            try
            {
                bool? auditSeen = null;
                string? valueSeen = "not called";
                var outcome = await JobProcess.RunTrackedAsync("Tracked", () =>
                {
                    auditSeen = AuditContext.Current != null;
                    valueSeen = _requestValue.Value;
                    return Task.FromResult<string?>("ok");
                }, allowConcurrent: true);
                Assert.That(outcome!.Succeeded, Is.True);
                Assert.That(auditSeen, Is.False, "ジョブの中で要求の AuditContext.Current が見えている");
                Assert.That(valueSeen, Is.Null);

                //呼び出し側の値は変わらない
                Assert.That(AuditContext.Current, Is.Not.Null);
                Assert.That(_requestValue.Value, Is.EqualTo("request"));
            }
            finally
            {
                AuditContext.Current = null;
                _requestValue.Value = null;
            }
        }

        [Test]
        public async Task 非同期_要求のAsyncLocalをジョブに引き継がない()
        {
            var seen = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _requestValue.Value = "request";
            try
            {
                await JobProcess.StartTrackedAsync("Tracked", () =>
                {
                    seen.SetResult(_requestValue.Value);
                    return Task.FromResult<string?>(null);
                }, "u", allowConcurrent: true);
            }
            finally
            {
                _requestValue.Value = null;
            }
            Assert.That(await seen.Task, Is.Null);
        }

        //カルチャ (エラー・文言の言語) は起動したユーザーのものを引き継ぐ。同期・非同期とも
        [Test]
        public async Task 同期と非同期_カルチャは引き継ぐ()
        {
            var culture = CultureInfo.CurrentCulture;
            var uiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            try
            {
                string? syncSeen = null;
                await JobProcess.RunTrackedAsync("Tracked", () =>
                {
                    syncSeen = $"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}";
                    return Task.FromResult<string?>(null);
                }, allowConcurrent: true);
                Assert.That(syncSeen, Is.EqualTo("fr-FR|de-DE"));

                var asyncSeen = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                await JobProcess.StartTrackedAsync("Tracked", async () =>
                {
                    //await をまたいでも保たれる
                    await Task.Yield();
                    asyncSeen.SetResult($"{CultureInfo.CurrentCulture.Name}|{CultureInfo.CurrentUICulture.Name}");
                    return null;
                }, "u", allowConcurrent: true);
                Assert.That(await asyncSeen.Task, Is.EqualTo("fr-FR|de-DE"));
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = uiCulture;
            }
        }

        //テスト用ホスト (Test.csproj の ProjectReference でこのテストの bin に写る)
        static string HostDll
        {
            get
            {
                var dll = Path.Combine(Path.GetDirectoryName(typeof(JobTrackedRunTest).Assembly.Location)!, "Codeer.LowCode.Blazor.Extras.Test.JobHost.dll");
                Assert.That(File.Exists(dll), Is.True, dll);
                return dll;
            }
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
                await Task.Delay(20);
            }
        }

        static async Task WaitAsync(Func<Task<bool>> condition)
        {
            for (var i = 0; i < 300; i++)
            {
                if (await condition()) return;
                await Task.Delay(50);
            }
            Assert.Fail("timeout");
        }

        class ListLoggerProvider(List<string> logs, List<LogLevel> levels) : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new ListLogger(logs, levels);
            public void Dispose() { }

            class ListLogger(List<string> logs, List<LogLevel> levels) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (logs)
                    {
                        logs.Add(formatter(state, exception));
                        levels.Add(logLevel);
                    }
                }
            }
        }
    }
}
