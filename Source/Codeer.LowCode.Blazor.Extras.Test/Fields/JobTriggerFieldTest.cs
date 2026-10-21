using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Jobs;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// JobTriggerField のランタイム: ホストに送る依頼 (JSON 3 種)・入力チェック・応答の通知と戻り値・実行中の二重実行。
    /// ホスト (JobTriggerField.RunCoreAsync) と通知 (IUIService) は記録するだけの偽物。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobTriggerFieldTest
    {
        class RecordingUIService : IUIService
        {
            public List<string> Successes { get; } = new();
            public List<string> Errors { get; } = new();
            public Task NotifySuccess(string message) { Successes.Add(message); return Task.CompletedTask; }
            public Task NotifyError(string message) { Errors.Add(message); return Task.CompletedTask; }
            public Task CloseDialog(Module moduleData, string button) => Task.CompletedTask;
            public Task ClosePanel(Module module, string button) => Task.CompletedTask;
            public Task ClosePopup(Module module, string button) => Task.CompletedTask;
            public Task<string> ShowDialog(Module moduleData, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPopup(Module moduleData, int x, int y, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPanel(Module moduleData, DialogButton[] buttons, PanelAlignment alignment) => Task.FromResult(string.Empty);
            public Task<string> ShowMessageBox(string title, string message, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<bool> DownloadFile(MemoryStream stream, string name) => Task.FromResult(true);
        }

        class Host
        {
            public HarnessAppService App { get; init; } = null!;
            public RecordingUIService UI { get; init; } = null!;
            public List<JobRunRequest> Requests { get; } = new();
            public Func<JobRunRequest, Task<JobRunResponse?>> OnRunJob { get; set; } = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Succeeded = true });
        }

        Func<JobTriggerField, JobRunRequest, Task<JobRunResponse?>>? _runCore;
        string _endPoint = string.Empty;

        [SetUp]
        public void SetUp()
        {
            _runCore = JobTriggerField.RunCoreAsync;
            _endPoint = JobTriggerField.EndPoint;
        }

        [TearDown]
        public void TearDown()
        {
            JobTriggerField.RunCoreAsync = _runCore;
            JobTriggerField.EndPoint = _endPoint;
        }

        static GridRow Row(string fieldName)
            => new() { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = fieldName } } } };

        //表を持つモジュール (Id + Name + ジョブ) と、表を持たないモジュール (Name + ジョブ)
        static async Task<(Module Module, JobTriggerField Job, Host Host)> CreateAsync(
            bool withTable, bool useSubmitData = true, bool isAsynchronous = false, bool nameRequired = false)
        {
            var design = new ModuleDesign { Name = "Item", DataSourceName = "Test" };
            if (withTable)
            {
                design.DbTable = "T";
                design.CanCreate = true;
                design.CanUpdate = true;
                design.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            }
            design.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = withTable ? "Name" : string.Empty, IsRequired = nameRequired });
            design.Fields.Add(new JobTriggerFieldDesign { Name = "Job", JobName = "Sample", UseSubmitData = useSubmitData, IsAsynchronous = isAsynchronous });
            design.DetailLayouts[string.Empty].Layout = new GridLayoutDesign { Rows = [Row("Name"), Row("Job")] };
            var designData = new DesignData();
            designData.AddModule(design);

            var app = new HarnessAppService(designData);
            var ui = new RecordingUIService();
            var host = new Host { App = app, UI = ui };
            JobTriggerField.RunCoreAsync = (_, request) => { host.Requests.Add(request); return host.OnRunJob(request); };
            var core = new Codeer.LowCode.Blazor.RequestInterfaces.Services(new NullServiceProvider(), app, app, new DummyNavigationService(), ui, new ListLogger());
            var module = await ModuleCreationService.CreateModuleAsync(core, new ModuleData { Name = "Item" }, ModuleLayoutType.Detail, string.Empty);
            return (module, (JobTriggerField)module.GetField("Job")!, host);
        }

        class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        [Test]
        public async Task 変更があれば現在値のdataと送信内容のsubmitを送る()
        {
            var (module, job, host) = await CreateAsync(withTable: true);
            await ((TextField)module.GetField("Name")!).SetValueAsync("abc");

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.Requests, Has.Count.EqualTo(1));
            var request = host.Requests[0];
            Assert.That(request.ModuleName, Is.EqualTo("Item"));
            Assert.That(request.FieldName, Is.EqualTo("Job"));
            using var json = JsonDocument.Parse(request.Arguments);
            Assert.That(json.RootElement.EnumerateObject().Select(e => e.Name), Is.EqualTo(new[] { "data", "submit" }));
            var data = JsonConverterEx.DeserializeObject<ModuleData>(json.RootElement.GetProperty("data").GetRawText())!;
            Assert.That(data.Name, Is.EqualTo("Item"));
            Assert.That(((TextFieldData)data.Fields["Name"]).Value, Is.EqualTo("abc"));
            var submit = JsonConverterEx.DeserializeObject<List<ModuleSubmitData>>(json.RootElement.GetProperty("submit").GetRawText())!;
            Assert.That(submit, Has.Count.EqualTo(1));
            Assert.That(submit[0].ModuleName, Is.EqualTo("Item"));
            //新規 → Add に載る
            Assert.That(submit[0].Add, Has.Count.EqualTo(1));
            Assert.That(((TextFieldData)submit[0].Add[0].Fields["Name"]).Value, Is.EqualTo("abc"));
        }

        [Test]
        public async Task 送る内容が無ければsubmitはnull()
        {
            //表を持たないモジュールは送信内容が空 → 画面の現在値だけ
            var (module, job, host) = await CreateAsync(withTable: false);
            await ((TextField)module.GetField("Name")!).SetValueAsync("abc");

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.Requests, Has.Count.EqualTo(1));
            using var json = JsonDocument.Parse(host.Requests[0].Arguments);
            Assert.That(json.RootElement.EnumerateObject().Select(e => e.Name), Is.EqualTo(new[] { "data", "submit" }));
            Assert.That(json.RootElement.GetProperty("submit").ValueKind, Is.EqualTo(JsonValueKind.Null));
            var data = JsonConverterEx.DeserializeObject<ModuleData>(json.RootElement.GetProperty("data").GetRawText())!;
            Assert.That(((TextFieldData)data.Fields["Name"]).Value, Is.EqualTo("abc"));
        }

        [Test]
        public async Task UseSubmitDataがfalseなら空のJSONで入力チェックもしない()
        {
            var (_, job, host) = await CreateAsync(withTable: true, useSubmitData: false, nameRequired: true);

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.Requests, Has.Count.EqualTo(1));
            Assert.That(host.Requests[0].Arguments, Is.EqualTo("{}"));
        }

        [Test]
        public async Task 入力チェックが通らなければ頼まない()
        {
            var (_, job, host) = await CreateAsync(withTable: true, nameRequired: true);

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.Requests, Is.Empty);
            Assert.That(host.UI.Errors, Has.Count.EqualTo(1));
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task 成功なら既定の文言で成功の通知をしてtrue()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Succeeded = true });

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.UI.Successes.Single(), Does.Contain("Sample"));
            Assert.That(host.UI.Errors, Is.Empty);
        }

        [Test]
        public async Task 成功でMessageがあればそれを通知()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Succeeded = true, Message = "10 件を取り込みました" });

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.UI.Successes.Single(), Is.EqualTo("10 件を取り込みました"));
        }

        [Test]
        public async Task 失敗ならエラーの文言を通知してfalse()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Succeeded = false, Error = "在庫が足りません" });

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.UI.Successes, Is.Empty);
            Assert.That(host.UI.Errors.Single(), Is.EqualTo("在庫が足りません"));
        }

        [Test]
        public async Task 失敗でエラーが空なら既定の文言()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Succeeded = false, Message = "成功の文言は使わない" });

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.UI.Errors.Single(), Does.Contain("Sample"));
        }

        [Test]
        public async Task 非同期は開始の通知でtrue()
        {
            var (_, job, host) = await CreateAsync(withTable: false, isAsynchronous: true);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(new JobRunResponse { Started = true });

            Assert.That(await job.RunAsync(), Is.True);

            Assert.That(host.UI.Successes.Single(), Does.Contain("Sample"));
        }

        [Test]
        public async Task 通信の失敗は通知せずにfalse()
        {
            //null はホストが通知済み
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => Task.FromResult<JobRunResponse?>(null);

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.UI.Successes, Is.Empty);
            Assert.That(host.UI.Errors, Is.Empty);
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task ホストが例外を投げたらメッセージを通知しfalse()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            host.OnRunJob = _ => throw new InvalidOperationException("ジョブの受け口がありません");

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.UI.Errors.Single(), Is.EqualTo("ジョブの受け口がありません"));
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task 送り先が未設定なら通知してfalse()
        {
            //RunCoreAsync も EndPoint も無い (WPF / WinForms のテンプレート)
            var (_, job, host) = await CreateAsync(withTable: false);
            JobTriggerField.RunCoreAsync = null;
            JobTriggerField.EndPoint = string.Empty;

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.Requests, Is.Empty);
            Assert.That(host.UI.Errors.Single(), Does.Contain("JobTriggerField.EndPoint"));
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task 実行中は二重に頼まない()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            var gate = new TaskCompletionSource<JobRunResponse?>();
            host.OnRunJob = _ => gate.Task;

            var first = job.RunAsync();
            Assert.That(job.IsRunning, Is.True);

            Assert.That(await job.RunAsync(), Is.False);
            Assert.That(host.Requests, Has.Count.EqualTo(1));

            gate.SetResult(new JobRunResponse { Succeeded = true });
            Assert.That(await first, Is.True);
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task 書けない状態では頼まない()
        {
            //他のボタンと同じく ViewOnly (書き込み不可の権限・閲覧モード) では動かさない。通知も出さない
            var (_, job, host) = await CreateAsync(withTable: false);
            job.IsViewOnly = true;

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.Requests, Is.Empty);
            Assert.That(host.UI.Errors, Is.Empty);
            Assert.That(job.IsRunning, Is.False);
        }

        [Test]
        public async Task 無効なら頼まない()
        {
            var (_, job, host) = await CreateAsync(withTable: false);
            job.IsEnabled = false;

            Assert.That(await job.RunAsync(), Is.False);

            Assert.That(host.Requests, Is.Empty);
        }

        [Test]
        public async Task 保存には何も載せない()
        {
            var (_, job, _) = await CreateAsync(withTable: true);
            Assert.That(job.IsModified, Is.False);
            Assert.That(job.GetData(), Is.Null);
        }
    }
}
