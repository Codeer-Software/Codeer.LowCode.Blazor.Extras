using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 版表示: 埋め込みモジュール (ModuleField) も従属レコードとして ShowVersionAsync と同じ順で通る
    /// (差分 → スナップショットの複製をフォームに入れる → 差分から表示用の行 → 本体の ModuleField.ShowOwnedRecordsAsync)。
    /// 子モジュールに版の内容が入り、変わった項目が強調される。
    /// </summary>
    public class EditHistoryModuleFieldShowTest
    {
        static ModuleData Customer(string id, string name, string? note)
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Note"] = new TextFieldData { Value = note };
            return data;
        }

        static ModuleData Order(string title, ModuleData customer)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Customer"] = new ModuleFieldData { Id = EditHistorySnapshot.GetId(customer), Data = customer };
            return data;
        }

        [Test]
        public async Task 子モジュールに版の内容が入り_変わった項目だけ強調される()
        {
            var design = EditHistoryTestDesigns.Create(withCustomer: true);
            var services = new TestServices(design);
            services.App.ListProvider = _ => new Utils.Paging<ModuleData>();
            var previous = Order("A", Customer("5", "A社", "n"));
            var current = Order("A", Customer("5", "B社", "n"));

            var changes = EditHistoryDiff.Compute(design, design.Modules.Find("Order")!, previous, current, (_, _) => true);
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, new ModuleData { Name = "Order" }, ModuleLayoutType.None);
            await module.SetDataWithoutInteractionAsync(current.JsonClone());
            foreach (var (fieldDesign, owned) in EditHistoryContracts.OwnedRecords(design.Modules, design.Modules.Find("Order")!))
            {
                if (module.GetField(fieldDesign.Name) is IOwnedRecordsField ownedField && current.GetOwnedRows(owned.Name) is { } rows)
                {
                    var change = changes.FirstOrDefault(e => e.IsList && e.FieldName == owned.Name);
                    await ownedField.ShowOwnedRecordsAsync(owned.Name, OwnedRecordsDisplay.Build(rows, change));
                }
            }

            var child = module.GetField<ModuleField>("Customer")!.ChildModule!;
            Assert.That(child.GetIdText(), Is.EqualTo("5"));
            Assert.That(child.GetField<TextField>("Name")!.Value, Is.EqualTo("B社"));
            Assert.That(child.GetField("Name")!.ClassName, Is.EqualTo(EditHistoryField.ChangedClassName));
            Assert.That(child.GetField("Note")!.ClassName, Is.Empty);
            Assert.That(child.ClassName, Is.EqualTo(EditHistoryField.ChangedRowClassName), "行 (= 子レコード) の枠");
            Assert.That(module.GetField("Title")!.ClassName, Is.Empty);
        }
    }
}
