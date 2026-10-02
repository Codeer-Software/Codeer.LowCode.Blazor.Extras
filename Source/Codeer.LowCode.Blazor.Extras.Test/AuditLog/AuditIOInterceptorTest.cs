using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    /// <summary>
    /// 保存の合流点に置くインターセプタ。監査ログのテーブルへの保存を拒否し、保存の対象を今のリクエストの監査レコードに記録する。
    /// </summary>
    public class AuditIOInterceptorTest
    {
        static readonly AuditLogDatabaseSettings Settings = new() { DataSourceName = "Audit", Table = "audit_log" };

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            design.AddModule(new ModuleDesign { Name = "AuditView", DataSourceName = "audit", DbTable = "AUDIT_LOG" });
            design.AddModule(new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customer" });
            return design;
        }

        static ModuleData Row(string module, string id)
            => new() { Name = module, Fields = { [SystemFieldNames.Id] = new IdFieldData { Value = id } } };

        static ModuleSubmitData Submit(string module) => new() { ModuleName = module, Id = "1", Update = { Row(module, "1") } };

        [TearDown]
        public void TearDown() => AuditContext.Current = null;

        [Test]
        public async Task RejectsSavesToTheAuditTable_CaseInsensitive()
        {
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);
            var nextCalled = false;
            var results = await interceptor.SubmitAsync(null!, [Submit("Customer"), Submit("AuditView")], () => { nextCalled = true; return Task.FromResult(new List<ModuleSubmitResult>()); });

            Assert.That(nextCalled, Is.False);
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.All(r => r.ExceptionMessage.Contains("append-only") && r.ExceptionMessage.Contains("AuditView")));
        }

        [Test]
        public async Task PassesOtherModules()
        {
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);
            var expected = new List<ModuleSubmitResult> { new() { SourceId = "1", DestinationId = "1" } };
            var results = await interceptor.SubmitAsync(null!, [Submit("Customer")], () => Task.FromResult(expected));
            Assert.That(results, Is.SameAs(expected));
        }

        [Test]
        public async Task ChildRowsAreAlsoChecked()
        {
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);
            var submit = new ModuleSubmitData { ModuleName = "Customer", Id = "1", Delete = { new ModuleDeleteInfo { ModuleName = "AuditView", Id = "9" } } };
            var results = await interceptor.SubmitAsync(null!, [submit], () => Task.FromResult(new List<ModuleSubmitResult>()));
            Assert.That(results.Single().ExceptionMessage, Does.Contain("AuditView"));
        }

        [Test]
        public async Task NothingIsProtectedWithoutADatabaseSink()
        {
            //DB に出力していない (ファイルだけ) 構成では、守るテーブルは無い
            var interceptor = new AuditIOInterceptor(CreateDesign(), new AuditLogDatabaseSettings());
            var expected = new List<ModuleSubmitResult> { new() { SourceId = "1", DestinationId = "1" } };
            Assert.That(await interceptor.SubmitAsync(null!, [Submit("AuditView")], () => Task.FromResult(expected)), Is.SameAs(expected));
        }

        [Test]
        public async Task RecordsTheTargetsOfTheCurrentRequest()
        {
            var audit = new AuditContext();
            AuditContext.Current = audit;
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);

            await interceptor.SubmitAsync(null!, [Submit("Customer")], () => Task.FromResult(new List<ModuleSubmitResult> { new() { SourceId = "1", DestinationId = "1" } }));

            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Customer", "1", "Update") }));
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=0; Update=1; Delete=0"));
        }

        [Test]
        public async Task RejectedSavesAreRecordedAsFailureWithTheirTargets()
        {
            var audit = new AuditContext();
            AuditContext.Current = audit;
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);

            await interceptor.SubmitAsync(null!, [Submit("AuditView")], () => Task.FromResult(new List<ModuleSubmitResult>()));

            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(audit.Event.Detail, Does.Contain("append-only"));
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("AuditView", "1", "Update") }));
        }

        [Test]
        public async Task AccessDeniedSavesAreRecordedAsDeniedWithTheirTargets()
        {
            //本体の権限拒否は型のままインターセプタまで上がる。拒否として対象ごと残し、例外はそのまま上げる (本体が ExceptionMessage にする)
            var audit = new AuditContext();
            AuditContext.Current = audit;
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);

            Assert.ThrowsAsync<LowCodeAccessDeniedException>(async () =>
                await interceptor.SubmitAsync(null!, [Submit("Customer")], () => throw LowCodeAccessDeniedException.Create("no permission")));

            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Denied));
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=0; Update=1; Delete=0; no permission"));
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Customer", "1", "Update") }));
        }

        [Test]
        public async Task SavesOutsideARequestAreNotRecorded()
        {
            //バックグラウンドのジョブ・監査ログが無効のとき (Current が無い) は、送信に手を加えずそのまま通す
            var interceptor = new AuditIOInterceptor(CreateDesign(), Settings);
            var submit = new ModuleSubmitData { ModuleName = "Customer", Add = { new ModuleData { Name = "Customer" } } };
            await interceptor.SubmitAsync(null!, [submit], () => Task.FromResult(new List<ModuleSubmitResult>()));
            Assert.That(submit.Id, Is.Null.Or.Empty, "仮 Id を付けない");
        }
    }
}
