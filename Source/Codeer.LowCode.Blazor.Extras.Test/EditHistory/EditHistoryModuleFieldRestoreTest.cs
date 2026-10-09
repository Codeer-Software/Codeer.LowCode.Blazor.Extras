using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 埋め込みモジュール (ModuleField) の復元。本体の従属レコード宣言により明細と同じ経路 (Module.ApplyRecordAsync → ModuleField.ApplyOwnedRecordsAsync):
    /// 子モジュールへ項目ごとに反映し (システムフィールドは触らない)、親の保存に子の Update が乗る。版の子が別レコードなら親の参照ごと戻す。
    /// </summary>
    public class EditHistoryModuleFieldRestoreTest
    {
        static ModuleData Customer(string id, string? name, string? note, string lockValue = "9")
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Note"] = new TextFieldData { Value = note };
            data.Fields["OptimisticLocking"] = new OptimisticLockingFieldData { Value = MultiTypeValue.Create(lockValue) };
            return data;
        }

        //今の状態 (画面の読み出しと同じ形: 参照 + 子の内容)
        static ModuleData Current(ModuleData customer)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = "A" };
            data.Fields["Customer"] = new ModuleFieldData { Id = EditHistorySnapshot.GetId(customer), Data = customer };
            return data;
        }

        //版 (記録の形も同じ: 参照 + 子の内容。子が無ければ参照なし)
        static ModuleData Snapshot(ModuleData? customer)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = "A" };
            data.Fields["Customer"] = customer == null ? new ModuleFieldData() : new ModuleFieldData { Id = EditHistorySnapshot.GetId(customer), Data = customer };
            return data;
        }

        static async Task<(Module Module, ModuleField Field)> CreateOrderAsync(ModuleData current)
        {
            var services = new TestServices(EditHistoryTestDesigns.Create(withCustomer: true));
            services.App.ListProvider = _ => new Utils.Paging<ModuleData>();
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, current, ModuleLayoutType.None);
            return (module, module.GetField<ModuleField>("Customer")!);
        }

        [Test]
        public async Task 子レコードの項目が戻り_親の保存に子のUpdateが乗る_システム項目は送らない()
        {
            //今: 顧客 5 = "B社" / 備考 "x" (楽観ロック 9)。版: 顧客 5 = "A社" / 備考は空
            var (module, field) = await CreateOrderAsync(Current(Customer("5", "B社", "x")));
            var applied = await EditHistoryRestorer.ApplyAsync(module, Snapshot(Customer("5", "A社", null, lockValue: "1")), null);
            Assert.That(applied, Is.GreaterThan(0));

            var child = field.ChildModule!;
            Assert.That(child.GetIdText(), Is.EqualTo("5"));
            Assert.That(child.GetField<TextField>("Name")!.Value, Is.EqualTo("A社"));
            Assert.That(child.GetField<TextField>("Note")!.Value, Is.Null, "版で空だった項目は空に戻る");

            var update = module.GetSubmitData().Update.Single(e => e.Name == "Customer");
            Assert.That(((IdFieldData)update.Fields["Id"]).Value, Is.EqualTo("5"));
            Assert.That(((TextFieldData)update.Fields["Name"]).Value, Is.EqualTo("A社"));
            Assert.That(update.Fields.TryGetValue("OptimisticLocking", out var lockData) && ((OptimisticLockingFieldData)lockData).Value?.GetValue()?.ToString() == "1",
                Is.False, "版の楽観ロック値を子に入れない");
        }

        [Test]
        public async Task 版の子が別レコードなら親の参照ごと戻る()
        {
            //今: 顧客 6。版: 顧客 5 ("A社")
            var (module, field) = await CreateOrderAsync(Current(Customer("6", "C社", null)));
            await EditHistoryRestorer.ApplyAsync(module, Snapshot(Customer("5", "A社", null)), null);

            Assert.That(field.ChildModule!.GetIdText(), Is.EqualTo("5"));
            Assert.That(field.ChildModule!.GetField<TextField>("Name")!.Value, Is.EqualTo("A社"));
            var order = module.GetSubmitData().Update.Single(e => e.Name == "Order");
            Assert.That(((ModuleFieldData)order.Fields["Customer"]).Id, Is.EqualTo("5"), "親の参照 (FK) が版の子に戻る");
        }

        [Test]
        public async Task 版に子が無ければ触らない()
        {
            var (module, field) = await CreateOrderAsync(Current(Customer("5", "B社", "x")));
            await EditHistoryRestorer.ApplyAsync(module, Snapshot(null), null);

            Assert.That(field.IsModified, Is.False);
            Assert.That(field.ChildModule?.GetField<TextField>("Name")?.Value ?? "B社", Is.EqualTo("B社"));
            Assert.That(module.GetSubmitData().Update.Any(e => e.Name == "Customer"), Is.False);
        }
    }
}
