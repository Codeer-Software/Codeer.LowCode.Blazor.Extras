using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    public class AuditTableGuardTest
    {
        static readonly AuditLogDatabaseSettings Settings = new() { DataSourceName = "Audit", Table = "audit_log" };

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            design.AddModule(new ModuleDesign { Name = "AuditView", DataSourceName = "audit", DbTable = "AUDIT_LOG" });
            design.AddModule(new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customer" });
            return design;
        }

        static ModuleSubmitData Submit(string module) => new() { ModuleName = module, Id = "1", Update = { new ModuleData { Name = module } } };

        [Test]
        public async Task RejectsSavesToTheAuditTable_CaseInsensitive()
        {
            var guard = new AuditTableGuard(CreateDesign(), Settings);
            var nextCalled = false;
            var results = await guard.SubmitAsync(null!, [Submit("Customer"), Submit("AuditView")], () => { nextCalled = true; return Task.FromResult(new List<ModuleSubmitResult>()); });

            Assert.That(nextCalled, Is.False);
            Assert.That(results, Has.Count.EqualTo(2));
            Assert.That(results.All(r => r.ExceptionMessage.Contains("append-only") && r.ExceptionMessage.Contains("AuditView")));
        }

        [Test]
        public async Task PassesOtherModules()
        {
            var guard = new AuditTableGuard(CreateDesign(), Settings);
            var expected = new List<ModuleSubmitResult> { new() { SourceId = "1", DestinationId = "1" } };
            var results = await guard.SubmitAsync(null!, [Submit("Customer")], () => Task.FromResult(expected));
            Assert.That(results, Is.SameAs(expected));
        }

        [Test]
        public async Task ChildRowsAreAlsoChecked()
        {
            var guard = new AuditTableGuard(CreateDesign(), Settings);
            var submit = new ModuleSubmitData { ModuleName = "Customer", Id = "1", Delete = { new ModuleDeleteInfo { ModuleName = "AuditView", Id = "9" } } };
            var results = await guard.SubmitAsync(null!, [submit], () => Task.FromResult(new List<ModuleSubmitResult>()));
            Assert.That(results.Single().ExceptionMessage, Does.Contain("AuditView"));
        }
    }
}
