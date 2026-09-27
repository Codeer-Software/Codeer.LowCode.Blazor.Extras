using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 版表示ダイアログでの従属レコードの強調: 版の行と差分を行 Id で対応づけて表示用の行 (OwnedRecordRow) を組み立て、
    /// 本体の ListField / 拡張フィールドがそれを行モジュールに写す。削除行は前の版の行を元の位置に差し込む。
    /// </summary>
    public class EditHistoryOwnedDisplayTest
    {
        static ModuleData Item(string id, string name, params ModuleData[] children)
        {
            var data = new ModuleData { Name = "OrderItem" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            if (children.Length > 0) data.Fields["Details"] = new ListFieldData { Children = children.ToList() };
            return data;
        }

        static EditHistoryRowChange Row(EditHistoryRowChangeKind kind, int number, ModuleData row, params EditHistoryChange[] changes)
            => new() { Kind = kind, RowNumber = number, Row = row, Changes = changes.ToList() };

        [Test]
        public void 追加は行全体_変更は行と変わったセル_削除は前の版の行を元の位置に差し込む()
        {
            var rows = new List<ModuleData> { Item("1", "A"), Item("2", "B"), Item("3", "C") };
            var change = new EditHistoryChange
            {
                FieldName = "Items", IsList = true,
                Rows =
                [
                    Row(EditHistoryRowChangeKind.Changed, 2, rows[1], new EditHistoryChange { FieldName = "Name", DisplayName = "品名", Before = "b", After = "B" }),
                    Row(EditHistoryRowChangeKind.Added, 3, rows[2]),
                    Row(EditHistoryRowChangeKind.Removed, 2, Item("9", "old")),
                    Row(EditHistoryRowChangeKind.Removed, 2, Item("9", "dup")),
                ],
            };

            var shown = OwnedRecordsDisplay.Build(rows, change);

            Assert.That(shown.Select(e => EditHistorySnapshot.GetId(e.Data)), Is.EqualTo(new[] { "1", "9", "2", "3" }), "削除行は前の版の位置 (行 2) に 1 回だけ差し込む");
            Assert.That(shown[0].ClassName, Is.Empty, "差分の無い行は装飾しない");
            Assert.That(shown[1].ClassName, Is.EqualTo(EditHistoryField.RemovedRowClassName));
            Assert.That(shown[2].ClassName, Is.EqualTo(EditHistoryField.ChangedRowClassName));
            Assert.That(shown[2].FieldClassNames, Is.EqualTo(new Dictionary<string, string> { ["Name"] = EditHistoryField.ChangedClassName }));
            Assert.That(shown[3].ClassName, Is.EqualTo(EditHistoryField.AddedRowClassName));
            Assert.That(shown[3].FieldClassNames, Is.Empty, "追加行はセルを個別に強調しない");
        }

        [Test]
        public void 差分が無ければ版の行だけで装飾なし()
        {
            var shown = OwnedRecordsDisplay.Build([Item("1", "A")], null);
            Assert.That(shown, Has.Count.EqualTo(1));
            Assert.That(shown[0].ClassName, Is.Empty);
            Assert.That(shown[0].OwnedRecords, Is.Empty);
        }

        [Test]
        public void 孫の差分は変更行の中に再帰し_削除行の孫は全部打ち消し()
        {
            var grand = Item("21", "g1");
            var row = Item("2", "B", grand, Item("22", "g2"));
            var nested = new EditHistoryChange
            {
                FieldName = "Details", IsList = true,
                Rows = [Row(EditHistoryRowChangeKind.Added, 2, row.Fields["Details"] is ListFieldData l ? l.Children[1] : grand)],
            };
            var change = new EditHistoryChange
            {
                FieldName = "Items", IsList = true,
                Rows =
                [
                    Row(EditHistoryRowChangeKind.Changed, 1, row, nested),
                    Row(EditHistoryRowChangeKind.Removed, 2, Item("9", "old", Item("91", "og"))),
                ],
            };

            var shown = OwnedRecordsDisplay.Build([row], change);

            var changed = shown[0];
            Assert.That(changed.ClassName, Is.EqualTo(EditHistoryField.ChangedRowClassName));
            Assert.That(changed.FieldClassNames, Is.Empty, "一覧の差分はセルではなく孫の行に付く");
            var grandRows = changed.OwnedRecords["Details"];
            Assert.That(grandRows.Select(e => e.ClassName), Is.EqualTo(new[] { "", EditHistoryField.AddedRowClassName }));

            var removed = shown[1];
            Assert.That(removed.ClassName, Is.EqualTo(EditHistoryField.RemovedRowClassName));
            Assert.That(removed.OwnedRecords["Details"].Single().ClassName, Is.EqualTo(EditHistoryField.RemovedRowClassName));
        }

        [Test]
        public async Task 本体の一覧は版の行をDBを読まずに見せ_行とセルのクラスを写す()
        {
            var services = new TestServices(EditHistoryTestDesigns.Create());
            var loads = 0;
            services.App.ListProvider = _ => { loads++; return new Utils.Paging<ModuleData>(); };
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, order, ModuleLayoutType.None);
            var items = module.GetField<ListField>("Items")!;
            Assert.That(items, Is.InstanceOf<IOwnedRecordsField>());

            var rows = new List<OwnedRecordRow>
            {
                new() { Data = Item("10", "X") },
                new() { Data = Item("11", "Y"), ClassName = EditHistoryField.ChangedRowClassName, FieldClassNames = { ["Name"] = EditHistoryField.ChangedClassName } },
                new() { Data = Item("12", "Z"), ClassName = EditHistoryField.AddedRowClassName },
            };
            await ((IOwnedRecordsField)items).ShowOwnedRecordsAsync("Items", rows);

            Assert.That(loads, Is.EqualTo(0));
            Assert.That(items.Rows.Select(e => e.GetIdText()), Is.EqualTo(new[] { "10", "11", "12" }));
            Assert.That(items.Rows.Select(e => e.ClassName), Is.EqualTo(new[] { "", EditHistoryField.ChangedRowClassName, EditHistoryField.AddedRowClassName }));
            Assert.That(items.Rows[1].GetField("Name")!.ClassName, Is.EqualTo(EditHistoryField.ChangedClassName));
            Assert.That(items.Rows[1].GetField<TextField>("Name")!.Value, Is.EqualTo("Y"));
        }

        [Test]
        public async Task 復元は反映したフィールド数を返し_反映できるものが無ければ0()
        {
            var services = new TestServices(EditHistoryTestDesigns.Create());
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = "1" };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, order, ModuleLayoutType.None);

            var nothing = new ModuleData { Name = "Order" };
            nothing.Fields["Id"] = new IdFieldData { Value = "1" };
            nothing.Fields["Unknown"] = new TextFieldData { Value = "x" };
            Assert.That(await EditHistoryRestorer.ApplyAsync(module, nothing, null), Is.EqualTo(0), "Id は対象外・無いフィールドは無視");

            var some = new ModuleData { Name = "Order" };
            some.Fields["Title"] = new TextFieldData { Value = "T" };
            some.Fields["Amount"] = new NumberFieldData { Value = 1 };
            Assert.That(await EditHistoryRestorer.ApplyAsync(module, some, null), Is.EqualTo(2));
        }

        [Test]
        public void 一覧は親の画面で編集できれば従属_参照するだけなら従属でない()
        {
            var d = EditHistoryTestDesigns.Create();
            var order = d.Modules.Find("Order")!;
            var related = (ListFieldDesign)order.Fields.Single(e => e.Name == "Related");
            Assert.That(related.DeleteTogether, Is.False);
            Assert.That(related.GetOwnedRecords(), Is.Empty, "参照するだけ (Can* 全部 false) の一覧は親の保存に乗らない");

            related.CanUpdate = true;
            Assert.That(related.GetOwnedRecords().Select(e => e.Name), Is.EqualTo(new[] { "Related" }), "行を編集できる一覧は DeleteTogether でなくても親の版に入る");
            Assert.That(EditHistoryContracts.OwnedRecords(order).Select(e => e.Owned.Name), Is.EquivalentTo(new[] { "Items", "Related" }));
        }

        [Test]
        public async Task 版でnullだった項目は復元でnullに戻る()
        {
            var services = new TestServices(EditHistoryTestDesigns.Create());
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = "1" };
            order.Fields["Amount"] = new NumberFieldData { Value = 500 };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, order, ModuleLayoutType.None);
            Assert.That(module.GetField<NumberField>("Amount")!.Value, Is.EqualTo(500));

            var snapshot = new ModuleData { Name = "Order" };
            snapshot.Fields["Amount"] = new NumberFieldData();
            Assert.That(await EditHistoryRestorer.ApplyAsync(module, snapshot, null), Is.EqualTo(1));
            Assert.That(module.GetField<NumberField>("Amount")!.Value, Is.Null);
        }

        [Test]
        public async Task 対象レコードリンクは更新の版で対象の詳細URL_削除の版と対象モジュール無しでは空()
        {
            var design = EditHistoryTestDesigns.Create();
            var history = design.Modules.Find("EditHistory")!;
            history.Fields.Add(new EditHistoryTargetLinkFieldDesign { Name = "Open" });
            var services = new TestServices(design);

            async Task<string> UrlOf(string moduleName, string changeType)
            {
                var row = new ModuleData { Name = "EditHistory" };
                row.Fields["Id"] = new IdFieldData { Value = "100" };
                row.Fields["ModuleName"] = new TextFieldData { Value = moduleName };
                row.Fields["DataId"] = new TextFieldData { Value = "5" };
                row.Fields["ChangeType"] = new TextFieldData { Value = changeType };
                var module = await ModuleCreationService.CreateModuleAsync(services.Core, row, ModuleLayoutType.None);
                return module.GetField<EditHistoryTargetLinkField>("Open")!.TargetUrl;
            }

            Assert.That(await UrlOf("Order", "Update"), Is.EqualTo("/Main/Order/5"));
            Assert.That(await UrlOf("Order", "Add"), Is.EqualTo("/Main/Order/5"));
            Assert.That(await UrlOf("Order", "Delete"), Is.Empty, "削除の版のレコードはもう開けない");
            Assert.That(await UrlOf("Nothing", "Update"), Is.Empty);
        }
    }
}
