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
    /// <summary>版表示の流れ (スナップショットを入れる → 差分から表示用の行を作る → 一覧に見せる) を ShowVersionAsync と同じ順で通す。</summary>
    public class EditHistoryShowVersionFlowTest
    {
        static ModuleData Order(string title, params ModuleData[] items)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = title };
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

        [Test]
        public async Task 明細の変更行と変わったセルと削除行が一覧に出る()
        {
            var design = EditHistoryTestDesigns.Create();
            var services = new TestServices(design);
            services.App.ListProvider = _ => new Utils.Paging<ModuleData>();
            var previous = Order("A", Item("10", "X", 2), Item("11", "Y", 1));
            var current = Order("A", Item("10", "X", 5), Item("12", "Z", 1));

            //ShowVersionAsync と同じ順: 差分は先に計算済み → スナップショットの複製をフォームに入れる → 差分から表示用の行 → 一覧に見せる
            var changes = EditHistoryDiff.Compute(design, design.Modules.Find("Order")!, previous, current, _ => true);
            var change = changes.Single(e => e.IsList && e.FieldName == "Items");

            var module = await ModuleCreationService.CreateModuleAsync(services.Core, new ModuleData { Name = "Order" }, ModuleLayoutType.None);
            foreach (var list in module.GetFields().OfType<ListField>()) list.AllowLoad = false;
            //本体の ListField.SetDataAsync は渡した行データの Id を消す (新しい行にする) ので複製を渡す
            await module.SetDataWithoutInteractionAsync(current.JsonClone());

            var rows = OwnedRecordsDisplay.Build(((ListFieldData)current.Fields["Items"]).Children, change);
            Assert.That(rows.Select(e => e.ClassName), Is.EqualTo(new[] { EditHistoryField.ChangedRowClassName, EditHistoryField.RemovedRowClassName, EditHistoryField.AddedRowClassName }));

            var items = module.GetField<ListField>("Items")!;
            await ((IOwnedRecordsField)items).ShowOwnedRecordsAsync("Items", rows);

            Assert.That(items.Rows.Select(e => e.ClassName), Is.EqualTo(new[] { EditHistoryField.ChangedRowClassName, EditHistoryField.RemovedRowClassName, EditHistoryField.AddedRowClassName }));
            Assert.That(items.Rows[0].GetField("Qty")!.ClassName, Is.EqualTo(EditHistoryField.ChangedClassName));
        }
    }
}
