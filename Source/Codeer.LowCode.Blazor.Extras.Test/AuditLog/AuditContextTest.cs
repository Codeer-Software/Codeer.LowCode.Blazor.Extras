using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Test.AuditLog
{
    public class AuditContextTest
    {
        static ModuleData Row(string module, string id)
            => new() { Name = module, Fields = { [SystemFieldNames.Id] = new IdFieldData { Value = id } } };

        [Test]
        public async Task RecordSubmitResolvesAssignedIdsAndTakesTargetsBeforeSubmit()
        {
            var audit = new AuditContext();
            var tempRoot = IdFieldData.NewId().Value!;
            var tempChild = IdFieldData.NewId().Value!;
            var submit = new ModuleSubmitData
            {
                ModuleName = "Order", Id = tempRoot,
                Add = { Row("Order", tempRoot), Row("OrderItem", tempChild) },
                Update = { Row("Customer", "5") },
                Delete = { new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "9" } },
            };

            var results = await audit.RecordSubmitAsync([submit], () =>
            {
                //本体は保存の途中で新規行の Id を書き換える (ここでは消す) = 対象は保存前に取れていなければならない
                submit.Add.ForEach(e => e.Fields.Remove(SystemFieldNames.Id));
                return Task.FromResult(new List<ModuleSubmitResult>
                {
                    new() { SourceId = tempRoot, DestinationId = "301", TemporaryIdMap = { [tempChild] = "88" } },
                });
            });

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Success));
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[]
            {
                ("Order", "301", "Add"),
                ("OrderItem", "88", "Add"),
                ("Customer", "5", "Update"),
                ("OrderItem", "9", "Delete"),
            }));
        }

        [Test]
        public async Task RecordSubmitMarksResultErrorAsFailure()
        {
            var audit = new AuditContext();
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = "1", Update = { Row("Order", "1") } };
            await audit.RecordSubmitAsync([submit], () => Task.FromResult(new List<ModuleSubmitResult> { new() { SourceId = "1", DestinationId = "1", ExceptionMessage = "locked" } }));
            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(audit.Event.Detail, Is.EqualTo("locked"));
            Assert.That(audit.Event.Targets.Single().Id, Is.EqualTo("1"));
        }

        [Test]
        public void RecordSubmitKeepsTargetsWhenSubmitThrows()
        {
            var audit = new AuditContext();
            var temp = IdFieldData.NewId().Value!;
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = temp, Add = { Row("Order", temp) } };
            Assert.ThrowsAsync<InvalidOperationException>(() => audit.RecordSubmitAsync([submit], () => throw new InvalidOperationException("db down")));
            //結果が無いので仮 Id のまま残る (失敗の印はミドルウェアが例外から付ける)
            Assert.That(audit.Event.Targets.Single().Id, Is.EqualTo(temp));
        }
    }
}
