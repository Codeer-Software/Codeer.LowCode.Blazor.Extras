using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// EditHistoryField (クライアント): 履歴モジュールの読み方 (条件・並び・ページ)、版番号の採番、差分、
    /// スナップショットのフォームへの反映 (復元)。DB は使わず、ハーネスの一覧応答で駆動する。
    /// </summary>
    public class EditHistoryFieldHarnessTest
    {
        static ModuleData Order(string id, string title, decimal? amount, params ModuleData[] items)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Amount"] = new NumberFieldData { Value = amount };
            data.Fields["Items"] = new ListFieldData { Children = items.ToList() };
            return data;
        }

        static ModuleData Item(string id, string name, decimal qty)
        {
            var data = new ModuleData { Name = "OrderItem" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Qty"] = new NumberFieldData { Value = qty };
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

        //履歴 3 版 (新しい順に返す): v1 作成 / v2 件名変更 / v3 明細変更
        static readonly ModuleData _v1 = Order("1", "A", 100, Item("10", "X", 1));
        static readonly ModuleData _v2 = Order("1", "B", 100, Item("10", "X", 1));
        static readonly ModuleData _v3 = Order("1", "B", 100, Item("10", "X", 5), Item("12", "Z", 1));

        static List<ModuleData> Rows() =>
        [
            HistoryRow("103", "Update", _v3, new DateTime(2026, 9, 3)),
            HistoryRow("102", "Update", _v2, new DateTime(2026, 9, 2)),
            HistoryRow("101", "Add", _v1, new DateTime(2026, 9, 1)),
        ];

        static TestServices CreateServices(int pageSize = 20, bool logicalDelete = false)
        {
            var design = EditHistoryTestDesigns.Create(logicalDelete: logicalDelete);
            ((Extras.Designs.EditHistoryFieldDesign)design.Modules.Find("Order")!.Fields.First(e => e.Name == "History")).PageSize = pageSize;
            var services = new TestServices(design);
            services.App.ListProvider = request =>
            {
                var rows = Rows();
                var limit = request.Condition.LimitCount ?? rows.Count;
                return new Paging<ModuleData>
                {
                    TotalCount = rows.Count,
                    Items = rows.Skip(request.PageIndex * limit).Take(limit).ToList(),
                };
            };
            return services;
        }

        static async Task<(Module Module, EditHistoryField Field)> CreateOrderModuleAsync(TestServices services, ModuleData data)
        {
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.Detail);
            return (module, module.GetField<EditHistoryField>("History")!);
        }

        [Test]
        public async Task 詳細ページの初期化で履歴を読み_版番号は件数から採番し_差分を持つ()
        {
            var services = CreateServices();
            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(field.IsLoaded, Is.True);
            Assert.That(field.TotalCount, Is.EqualTo(3));
            Assert.That(field.HasMore, Is.False);
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(field.Versions.Select(e => e.ChangeType), Is.EqualTo(new[] { "Update", "Update", "Add" }));
            Assert.That(field.Versions[0].UserText, Is.EqualTo("user1"));
            Assert.That(field.Versions[0].DateTime, Is.EqualTo(new DateTime(2026, 9, 3)));

            //v3: 明細の変更と追加
            var v3 = field.Versions[0].Changes.Single();
            Assert.That((v3.DisplayName, v3.IsList, v3.ChangedCount, v3.AddedCount), Is.EqualTo(("明細", true, 1, 1)));
            //v2: 件名 A → B
            var v2 = field.Versions[1].Changes.Single();
            Assert.That((v2.DisplayName, v2.Before, v2.After), Is.EqualTo(("件名", "A", "B")));
            //v1: 作成の版は差分を出さない (「レコードが作成されました」)
            Assert.That(field.Versions[2].Changes, Is.Empty);
        }

        [Test]
        public async Task 履歴の検索条件はモジュール名とレコードIdで_新しい順()
        {
            var services = CreateServices();
            await CreateOrderModuleAsync(services, _v3);

            var requests = services.App.ListRequests;
            Assert.That(requests.Count, Is.EqualTo(2), "ページと、その次の 1 行 (末尾の版の差分の元)");
            var condition = requests[0].Condition;
            Assert.That(condition.ModuleName, Is.EqualTo("EditHistory"));
            var and = (MultiMatchCondition)condition.Condition!;
            Assert.That(and.Children.Cast<FieldValueMatchCondition>().Select(e => e.SearchTargetVariable), Is.EqualTo(new[] { "ModuleName.Value", "DataId.Value" }));
            Assert.That(condition.SortConditions.Select(e => (e.Variable, e.IsDescending)), Is.EqualTo(new[] { ("DateTime.Value", true), ("Id.Value", true) }));
            Assert.That(condition.LimitCount, Is.EqualTo(20));
            Assert.That(requests[1].Condition.LimitCount, Is.EqualTo(1));
            Assert.That(requests[1].PageIndex, Is.EqualTo(20));
        }

        [Test]
        public async Task ページ末尾の版の差分は次の1行を元に計算し_さらに表示で続きを読む()
        {
            var services = CreateServices(pageSize: 2);
            var (_, field) = await CreateOrderModuleAsync(services, _v3);

            Assert.That(field.Versions.Count, Is.EqualTo(2));
            Assert.That(field.HasMore, Is.True);
            var v2 = field.Versions[1].Changes.Single();
            Assert.That((v2.Before, v2.After), Is.EqualTo(("A", "B")), "v1 はページ外だが差分の元として読まれる");

            await field.LoadMoreAsync();
            Assert.That(field.Versions.Select(e => e.Number), Is.EqualTo(new[] { 3, 2, 1 }));
            Assert.That(field.HasMore, Is.False);
        }

        [Test]
        public async Task 新規レコードとデザインモードでは読まない()
        {
            var services = CreateServices();
            var module = await services.CreateModuleAsync("Order", ModuleLayoutType.Detail);
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(field.IsAvailable, Is.False);
            Assert.That(field.IsLoaded, Is.False);
            Assert.That(services.App.ListRequests, Is.Empty);
        }

        [Test]
        public async Task 復元は値を反映し_明細は行Idで更新追加削除する_保存はしない()
        {
            var services = CreateServices();
            var (module, field) = await CreateOrderModuleAsync(services, _v3);
            Assert.That(module.IsModified, Is.False);

            await EditHistoryRestorer.ApplyAsync(module, field.Versions[2].Snapshot!, null);  // v1 に戻す

            Assert.That(module.GetField<TextField>("Title")!.Value, Is.EqualTo("A"));
            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(1));
            var row = items.Rows[0];
            Assert.That(row.GetIdText(), Is.EqualTo("10"), "既存行は Id で突き合わせて更新");
            Assert.That(row.GetField<NumberField>("Qty")!.Value, Is.EqualTo(1));
            Assert.That(module.IsModified, Is.True, "保存はユーザーが行う (変更扱いになる)");
            Assert.That(module.GetField<IdField>("Id")!.Value, Is.EqualTo("1"));
        }

        [Test]
        public async Task 復元で消えていた明細行は新しい行として追加される()
        {
            var services = CreateServices();
            var (module, field) = await CreateOrderModuleAsync(services, _v2);

            await EditHistoryRestorer.ApplyAsync(module, field.Versions[0].Snapshot!, null);  // v3 へ

            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(2));
            Assert.That(items.Rows[0].GetField<NumberField>("Qty")!.Value, Is.EqualTo(5));
            var added = items.Rows[1];
            Assert.That(added.IsNewData, Is.True, "Id は振り直し");
            Assert.That(added.GetField<TextField>("Name")!.Value, Is.EqualTo("Z"));
        }

        [Test]
        public async Task 論理削除の明細は復元でIdを保って復活し_保存に削除の取り消しが同梱される()
        {
            var services = CreateServices(logicalDelete: true);
            var (module, field) = await CreateOrderModuleAsync(services, _v2);

            await field.RestoreAsync(field.Versions[0]);  // v3 へ (行 12 が無い → 論理削除なので Id を保って復活)
            //DummyUIService の ShowMessageBox は空文字を返すので RestoreAsync は中断する。復元本体を直接呼ぶ
            var revived = new List<(string, string)>();
            await EditHistoryRestorer.ApplyAsync(module, field.Versions[0].Snapshot!, (m, id) => revived.Add((m, id)));

            var items = module.GetField<ListField>("Items")!;
            Assert.That(items.Rows.Count, Is.EqualTo(2));
            var row = items.Rows[1];
            Assert.That(row.IsNewData, Is.False, "Id を保つ");
            Assert.That(row.GetIdText(), Is.EqualTo("12"));
            Assert.That(row.GetField<TextField>("Name")!.Value, Is.EqualTo("Z"));
            Assert.That(row.IsModified, Is.True, "値は Update として送られる");
            Assert.That(revived, Is.EqualTo(new[] { ("OrderItem", "12") }));
        }

        [Test]
        public void 削除の取り消しはExtendedDataで送られる()
        {
            var design = EditHistoryTestDesigns.Create(logicalDelete: true);
            var services = new TestServices(design);
            var module = ModuleCreationService.CreateModuleAsync(services.Core, _v3, ModuleLayoutType.None).Result;
            var field = module.GetField<EditHistoryField>("History")!;
            Assert.That(field.GetSubmitData().ExtendedData, Is.Empty);

            //復元で復活した行があるときだけ同梱される (フィールド自身は変更扱いにならない)
            var restorerField = typeof(EditHistoryField).GetField("_pendingUndeletes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            ((List<EditHistoryUndeleteTarget>)restorerField.GetValue(field)!).Add(new EditHistoryUndeleteTarget { ModuleName = "OrderItem", Id = "12" });
            var submit = field.GetSubmitData();
            var undelete = submit.ExtendedData.OfType<EditHistoryUndeleteData>().Single();
            Assert.That(undelete.Targets.Select(e => (e.ModuleName, e.Id)), Is.EqualTo(new[] { ("OrderItem", "12") }));
            Assert.That(field.IsModified, Is.False);
        }
    }
}
