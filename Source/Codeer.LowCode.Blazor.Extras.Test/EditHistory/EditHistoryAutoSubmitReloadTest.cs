using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 自動保存 (AutoSubmitField) との組み合わせ。自動保存はレコードを読み直さない軽量な保存で、
    /// 本体は保存後にフィールドの AcceptChanges だけを呼ぶ。EditHistoryField はそこで版を読み直す
    /// (通常の保存は再初期化で読み直されるので、こちらだけが取り残されていた)。
    /// </summary>
    public class EditHistoryAutoSubmitReloadTest
    {
        static ModuleData Order(string id, string title)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Items"] = new ListFieldData();
            return data;
        }

        static ModuleData HistoryRow(string id, string changeType, ModuleData snapshot, DateTime dateTime)
        {
            var data = new ModuleData { Name = "EditHistory" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["ChangeType"] = new TextFieldData { Value = changeType };
            data.Fields["Snapshot"] = new TextFieldData { Value = EditHistorySnapshot.Serialize(snapshot) };
            data.Fields["UserId"] = new TextFieldData { Value = "user1" };
            data.Fields["DateTime"] = new DateTimeFieldData { Value = dateTime };
            return data;
        }

        //履歴モジュールの応答 (新しい順)。保存のたびに先頭へ足す
        readonly List<ModuleData> _rows = new();

        [SetUp]
        public void SetUp() => _rows.Clear();

        TestServices CreateServices()
        {
            var services = new TestServices(EditHistoryTestDesigns.Create());
            services.App.CurrentUserData = new ModuleData { Name = "AppUser" };
            services.App.ListProvider = request =>
            {
                var limit = request.Condition.LimitCount ?? _rows.Count;
                return new Paging<ModuleData>
                {
                    TotalCount = _rows.Count,
                    Items = _rows.Skip(request.PageIndex * limit).Take(limit).ToList(),
                };
            };
            return services;
        }

        [Test]
        public async Task 自動保存の後に版を読み直す()
        {
            _rows.Add(HistoryRow("101", "Add", Order("1", "A"), new DateTime(2026, 9, 1)));
            var services = CreateServices();
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, Order("1", "A"), ModuleLayoutType.Detail);
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 1 }));
            var requestsBefore = services.App.ListRequests.Count;

            //自動保存 = 件名を変えて保存 → サーバーが版 2 を書く → 本体は AcceptChanges だけを呼ぶ (再初期化しない)
            await module.GetField<TextField>("Title")!.SetValueAsync("B");
            _rows.Insert(0, HistoryRow("102", "Update", Order("1", "B"), new DateTime(2026, 9, 2)));
            module.AcceptChanges(new SubmitAcceptInfo());
            Assert.That(field.ReloadAfterSubmit, Is.Not.Null, "保存をきっかけに読み直す");
            await field.ReloadAfterSubmit!;

            Assert.That(services.App.ListRequests.Count, Is.GreaterThan(requestsBefore), "履歴モジュールを読み直している");
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(field.Versions[0].ChangeType, Is.EqualTo("Update"));
            var change = field.Versions[0].Changes.Single();
            Assert.That((change.DisplayName, change.Before, change.After), Is.EqualTo(("件名", "A", "B")));
            Assert.That(services.Logger.ErrorList, Is.Empty);
        }

        [Test]
        public async Task 新規レコードの初回の自動保存で履歴が読めるようになる()
        {
            var services = CreateServices();
            //未保存 (Id なし) では履歴を読まない
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, new ModuleData { Name = "Order" }, ModuleLayoutType.Detail);
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(module.IsNewData, Is.True);
            Assert.That(field.IsAvailable, Is.False);
            Assert.That(field.IsLoaded, Is.False);

            //自動保存で作成 → 本体は採番 Id を入れてから AcceptChanges を呼ぶ (IsNewData が false になる)
            _rows.Add(HistoryRow("101", "Add", Order("1", "A"), new DateTime(2026, 9, 1)));
            await module.GetField<IdField>("Id")!.SetValueAsync("1");
            module.AcceptChanges(new SubmitAcceptInfo());
            Assert.That(module.IsNewData, Is.False);
            Assert.That(field.ReloadAfterSubmit, Is.Not.Null);
            await field.ReloadAfterSubmit!;

            Assert.That(field.IsLoaded, Is.True);
            Assert.That(field.Versions.Select(e => (e.Number, e.ChangeType)), Is.EqualTo(new[] { (1, "Add") }));
        }

        [Test]
        public async Task 一覧の行モジュールでは読まない()
        {
            _rows.Add(HistoryRow("101", "Add", Order("1", "A"), new DateTime(2026, 9, 1)));
            var services = CreateServices();
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, Order("1", "A"), ModuleLayoutType.List);
            var field = module.GetField<EditHistoryField>("History")!;
            var requestsBefore = services.App.ListRequests.Count;

            module.AcceptChanges(new SubmitAcceptInfo());

            Assert.That(field.ReloadAfterSubmit, Is.Null);
            Assert.That(services.App.ListRequests.Count, Is.EqualTo(requestsBefore), "行数分の要求になるので一覧の行では読まない (初期化と同じ)");
        }
    }
}
