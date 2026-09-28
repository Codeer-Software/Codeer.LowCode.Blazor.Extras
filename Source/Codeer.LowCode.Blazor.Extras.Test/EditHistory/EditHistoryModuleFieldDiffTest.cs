using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 埋め込みモジュール (ModuleField) の差分。本体が子レコードを従属として宣言するので明細と同じ経路:
    /// スナップショットでは参照の位置に子レコード 0 か 1 行の一覧が入り、行 Id で突き合わせて 追加 / 削除 / 変更 (項目ごと) が出る。
    /// </summary>
    public class EditHistoryModuleFieldDiffTest
    {
        static readonly DesignData _design = EditHistoryTestDesigns.Create(withCustomer: true);

        static ModuleData Customer(string id, string? name, string? note)
        {
            var data = new ModuleData { Name = "Customer" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Note"] = new TextFieldData { Value = note };
            data.Fields["OptimisticLocking"] = new OptimisticLockingFieldData { Value = MultiTypeValue.Create("1") };
            return data;
        }

        static ModuleData Order(string title, ModuleData? customer, params ModuleData[] items)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Customer"] = new ListFieldData { Children = customer == null ? new() : [customer] };
            data.Fields["Items"] = new ListFieldData { Children = items.ToList() };
            return data;
        }

        static ModuleData Item(string id, string name, ModuleData? supplier)
        {
            var data = new ModuleData { Name = "OrderItem" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Order"] = new LinkFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = name };
            data.Fields["Supplier"] = new ListFieldData { Children = supplier == null ? new() : [supplier] };
            return data;
        }

        static List<EditHistoryChange> Compute(ModuleData? before, ModuleData after)
            => EditHistoryDiff.Compute(_design, _design.Modules.Find("Order")!, before, after, _ => true);

        [Test]
        public void 子レコードの項目の変更は行1の変更として出る_変わらない項目とシステム項目は出ない()
        {
            var before = Order("A", Customer("5", "A社", "n1"));
            var after = Order("A", Customer("5", "B社", "n1"));
            ((OptimisticLockingFieldData)((ListFieldData)after.Fields["Customer"]).Children[0].Fields["OptimisticLocking"]).Value = MultiTypeValue.Create("2");

            var changes = Compute(before, after);
            Assert.That(changes.Count, Is.EqualTo(1));
            var change = changes[0];
            Assert.That((change.FieldName, change.IsList, change.ChangedCount), Is.EqualTo(("Customer", true, 1)));
            var row = change.Rows.Single();
            Assert.That((row.Kind, row.RowNumber), Is.EqualTo((EditHistoryRowChangeKind.Changed, 1)));
            Assert.That(row.Changes.Select(e => (e.DisplayName, e.Before, e.After)), Is.EqualTo(new[] { ("顧客名", "A社", "B社") }));
        }

        [Test]
        public void 子レコードが変わらなければ出ない()
        {
            var before = Order("A", Customer("5", "A社", "n1"));
            var after = Order("B", Customer("5", "A社", "n1"));
            Assert.That(Compute(before, after).Select(e => e.FieldName), Is.EqualTo(new[] { "Title" }));
        }

        [Test]
        public void 作成の版は子レコードが追加行として出る()
        {
            var changes = Compute(null, Order("A", Customer("5", "A社", null)));
            var change = changes.Single(e => e.FieldName == "Customer");
            Assert.That(change.AddedCount, Is.EqualTo(1));
            Assert.That(change.Rows.Single().Changes.Select(e => (e.DisplayName, e.After)), Is.EqualTo(new[] { ("顧客名", "A社") }));
        }

        [Test]
        public void 子の参照が付いた版は追加行_外れた版は削除行として出る()
        {
            var without = Order("A", null);
            var with = Order("A", Customer("5", "A社", null));
            Assert.That(Compute(without, with).Single().Rows.Single().Kind, Is.EqualTo(EditHistoryRowChangeKind.Added));
            var removed = Compute(with, without).Single().Rows.Single();
            Assert.That(removed.Kind, Is.EqualTo(EditHistoryRowChangeKind.Removed));
            Assert.That(removed.Changes.Select(e => (e.Before, e.After)), Is.EqualTo(new[] { ("A社", "") }));
        }

        [Test]
        public void 明細の削除行の中の埋め込みモジュールも打ち消し側に出る()
        {
            var before = Order("A", null, Item("10", "X", Customer("7", "仕入A", null)));
            var after = Order("A", null);
            var items = Compute(before, after).Single(e => e.FieldName == "Items");
            var removedItem = items.Rows.Single();
            Assert.That(removedItem.Kind, Is.EqualTo(EditHistoryRowChangeKind.Removed));
            var supplier = removedItem.Changes.Single(e => e.FieldName == "Supplier");
            Assert.That(supplier.IsList, Is.True);
            var removedSupplier = supplier.Rows.Single();
            Assert.That(removedSupplier.Kind, Is.EqualTo(EditHistoryRowChangeKind.Removed));
            Assert.That(removedSupplier.Changes.Select(e => (e.DisplayName, e.Before, e.After)), Is.EqualTo(new[] { ("顧客名", "仕入A", "") }));
        }
    }
}
