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

        static ModuleData NewRow(string module) => new() { Name = module };

        [Test]
        public void AddRead_RecordsTheModuleAndCount_AndRowIdsOnlyWhenAsked()
        {
            //参照は「誰が・どのモジュールを・何件」を常に残し、行の Id は recordIds のときだけ (閲覧の証跡)
            var page = new Codeer.LowCode.Blazor.Utils.Paging<ModuleData> { Items = { Row("Order", "1"), Row("Order", "2") } };

            var summary = new AuditContext();
            summary.AddRead("Order", page, recordIds: false);
            Assert.That(summary.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Order", (string?)null, "Read") }));
            Assert.That(summary.ComposeDetail(), Is.EqualTo("Rows=2"));

            var detailed = new AuditContext();
            detailed.AddRead("Order", page, recordIds: true);
            Assert.That(detailed.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Order", (string?)"1", "Read"), ("Order", "2", "Read") }));
            Assert.That(detailed.ComposeDetail(), Is.EqualTo("Rows=2"));
        }

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
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=2; Update=1; Delete=1"));
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
            //件数 → 理由の順
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=0; Update=1; Delete=0; locked"));
        }

        [Test]
        public void RecordSubmitKeepsTargetsWhenSubmitThrows()
        {
            var audit = new AuditContext();
            var temp = IdFieldData.NewId().Value!;
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = temp, Add = { Row("Order", temp) } };
            Assert.ThrowsAsync<InvalidOperationException>(() => audit.RecordSubmitAsync([submit], () => throw new InvalidOperationException("db down")));
            //結果が無いので採番 Id は分からない。仮 Id は残さず、モジュールと操作だけが残る (失敗の印はミドルウェアが例外から付ける)
            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Order", (string?)null, "Add") }));
        }

        [Test]
        public async Task NewRowsWithoutIdGetTheAssignedId()
        {
            //ファイル取込の新規行 (自動採番) は Id が空。仮 Id を付けて送り、1 行ずつの保存が返す採番 Id を引く
            var audit = new AuditContext();
            var submits = new List<ModuleSubmitData>
            {
                new() { ModuleName = "Item", NoTemporaryIdResolution = true, Add = { NewRow("Item") } },
                new() { ModuleName = "Item", NoTemporaryIdResolution = true, Add = { NewRow("Item") } },
            };

            var results = await audit.RecordSubmitAsync(submits, () =>
            {
                Assert.That(submits.All(e => IdFieldData.IsTemporaryId(e.Id)), Is.True, "Id の無い新規行に仮 Id が付いている");
                Assert.That(submits.All(e => e.NoTemporaryIdResolution), Is.True, "一括 INSERT の対象から外さない (性能を落とさない)");
                //本体の 1 行ずつの保存と同じ形: 対応表は全行の結果が同じものを持つ
                var map = submits.Select((e, i) => (e.Id, Real: (101 + i).ToString())).ToDictionary(e => e.Id, e => e.Real);
                return Task.FromResult(submits.Select(e => new ModuleSubmitResult { SourceId = e.Id, DestinationId = map[e.Id], TemporaryIdMap = map }).ToList());
            });

            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Item", "101", "Add"), ("Item", "102", "Add") }));
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=2; Update=0; Delete=0"));
            //監査のために付けた仮 Id は、呼び出し元へ返す結果に残さない (監査ログの有無で保存の応答を変えない)
            Assert.That(results.Select(r => (r.SourceId, r.DestinationId, r.TemporaryIdMap.Count)).ToArray(), Is.EqualTo(new[] { ("", "", 0), ("", "", 0) }));
            Assert.That(submits.All(e => e.Id == string.Empty), Is.True);
        }

        [Test]
        public async Task TemporaryIdsFromTheClientAreLeftAsTheyAre()
        {
            //画面の保存が付けた仮 Id (クライアントが採番 Id を受け取るのに使う) には手を付けない
            var audit = new AuditContext();
            var temp = IdFieldData.NewId().Value!;
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = temp, Add = { Row("Order", temp) } };
            var results = await audit.RecordSubmitAsync([submit], () => Task.FromResult(new List<ModuleSubmitResult>
            {
                new() { SourceId = temp, DestinationId = "301", TemporaryIdMap = { [temp] = "301" } },
            }));
            Assert.That((results[0].SourceId, results[0].DestinationId, results[0].TemporaryIdMap[temp]), Is.EqualTo((temp, "301", "301")));
            Assert.That(submit.Id, Is.EqualTo(temp));
        }

        [Test]
        public async Task BulkInsertedRowsAreRecordedByCountWithoutIds()
        {
            //本体の一括 INSERT は採番 Id を返さない (結果の DestinationId は仮 Id のまま)。Id 無しの Add をモジュールごとに 1 件と、件数が残る
            var audit = new AuditContext();
            var submits = Enumerable.Range(0, 300).Select(_ => new ModuleSubmitData { ModuleName = "Item", NoTemporaryIdResolution = true, Add = { NewRow("Item") } }).ToList();

            var results = await audit.RecordSubmitAsync(submits, () => Task.FromResult(submits.Select(e => new ModuleSubmitResult { SourceId = e.Id, DestinationId = e.Id }).ToList()));

            Assert.That(audit.Event.Targets.Select(t => (t.Module, t.Id, t.Operation)).ToArray(), Is.EqualTo(new[] { ("Item", (string?)null, "Add") }));
            Assert.That(results.All(r => r.SourceId == string.Empty && r.DestinationId == string.Empty), Is.True, "仮 Id は呼び出し元へ返す結果に残さない");
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Add=300; Update=0; Delete=0"));
        }

        [Test]
        public void DetailIsCountsThenNotesThenReason()
        {
            var audit = new AuditContext();
            audit.AddCount("Rows", 2);
            audit.AddNote("File", "abc");
            audit.AddCount("Rows", 3);
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Rows=5; File=abc"));
            audit.Fail("reason");
            Assert.That(audit.ComposeDetail(), Is.EqualTo("Rows=5; File=abc; reason"));
            Assert.That(new AuditContext().ComposeDetail(), Is.Empty);
        }
    }
}
