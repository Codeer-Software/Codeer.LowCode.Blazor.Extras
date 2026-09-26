using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    public class EditHistoryDiffTest
    {
        static readonly DesignData _design = EditHistoryTestDesigns.Create();

        static ModuleData Order(string id, string title, decimal? amount, params ModuleData[] items)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            data.Fields["Title"] = new TextFieldData { Value = title };
            data.Fields["Amount"] = new NumberFieldData { Value = amount };
            data.Fields["Secret"] = new TextFieldData { Value = "s" };
            data.Fields["OptimisticLocking"] = new OptimisticLockingFieldData { Value = MultiTypeValue.Create("1") };
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

        static List<EditHistoryChange> Compute(ModuleData? before, ModuleData after, Func<string, bool>? canRead = null)
            => EditHistoryDiff.Compute(_design, _design.Modules.Find("Order")!, before, after, canRead ?? (_ => true));

        [Test]
        public void 値の変更は表示名と旧新で出る_変わらない項目とシステム項目は出ない()
        {
            var before = Order("1", "A", 100);
            var after = Order("1", "B", 100);
            after.Fields["OptimisticLocking"] = new OptimisticLockingFieldData { Value = MultiTypeValue.Create("2") };
            var changes = Compute(before, after);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That((changes[0].DisplayName, changes[0].Before, changes[0].After), Is.EqualTo(("件名", "A", "B")));
        }

        [Test]
        public void 作成の版は値のある項目が全部出る()
        {
            var changes = Compute(null, Order("1", "A", null, Item("10", "X", 1)));
            Assert.That(changes.Select(e => e.FieldName), Is.EqualTo(new[] { "Title", "Secret", "Items" }));
            Assert.That(changes[0].Before, Is.Empty);
            Assert.That(changes[2].AddedCount, Is.EqualTo(1));
        }

        [Test]
        public void 明細は行Idで突き合わせて追加削除変更を出す()
        {
            var before = Order("1", "A", 100, Item("10", "X", 1), Item("11", "Y", 2));
            var after = Order("1", "A", 100, Item("10", "X", 5), Item("12", "Z", 1));
            var changes = Compute(before, after);
            Assert.That(changes.Count, Is.EqualTo(1));
            var list = changes[0];
            Assert.That(list.IsList, Is.True);
            Assert.That((list.AddedCount, list.RemovedCount, list.ChangedCount), Is.EqualTo((1, 1, 1)));
            var changed = list.Rows.Single(e => e.Kind == EditHistoryRowChangeKind.Changed);
            Assert.That(changed.RowNumber, Is.EqualTo(1), "行の見分けは番号 (名前を推測しない)");
            Assert.That((changed.Changes.Single().DisplayName, changed.Changes.Single().Before, changed.Changes.Single().After), Is.EqualTo(("数量", "1", "5")));
            var added = list.Rows.Single(e => e.Kind == EditHistoryRowChangeKind.Added);
            Assert.That(added.RowNumber, Is.EqualTo(2));
            Assert.That(added.Changes.Select(e => (e.DisplayName, e.After)), Is.EqualTo(new[] { ("品名", "Z"), ("数量", "1") }), "追加行は値を列挙");
            var removed = list.Rows.Single(e => e.Kind == EditHistoryRowChangeKind.Removed);
            Assert.That(removed.RowNumber, Is.EqualTo(2), "削除行は前の版での位置");
            Assert.That(removed.Changes.Select(e => (e.DisplayName, e.Before, e.After)), Is.EqualTo(new[] { ("品名", "Y", ""), ("数量", "2", "") }), "削除行は値を Before に");
            Assert.That(EditHistorySnapshot.GetId(added.Row!), Is.EqualTo("12"), "追加行のデータはこの版の行");
            Assert.That(EditHistorySnapshot.GetId(changed.Row!), Is.EqualTo("10"));
            Assert.That(EditHistorySnapshot.GetId(removed.Row!), Is.EqualTo("11"), "削除行のデータは前の版の行 (版表示で打ち消し表示に使う)");
        }

        //独自のデータクラス (文字列化の規約が無い型)
        class BlobFieldData : FieldDataBase
        {
            public BlobFieldData() : base(typeof(BlobFieldData).FullName!) { }
            public byte[] Bytes { get; set; } = [];
            public override bool Equals(object? obj) => obj is BlobFieldData o && Bytes.SequenceEqual(o.Bytes);
            public override int GetHashCode() => 0;
        }

        [Test]
        public void 文字列化できない型は変更の有無だけ出す()
        {
            var design = EditHistoryTestDesigns.Create();
            var order = design.Modules.Find("Order")!;
            order.Fields.Add(new Repository.Design.TextFieldDesign { Name = "Blob", DisplayName = "添付データ", DbColumn = "blob" });
            var before = Order("1", "A", 1); before.Fields["Blob"] = new BlobFieldData { Bytes = [1, 2] };
            var same = Order("1", "A", 1); same.Fields["Blob"] = new BlobFieldData { Bytes = [1, 2] };
            var changed = Order("1", "A", 1); changed.Fields["Blob"] = new BlobFieldData { Bytes = [3] };

            Assert.That(EditHistoryDiff.Compute(design, order, before, same, _ => true), Is.Empty, "JSON が同じなら変更なし");
            var changes = EditHistoryDiff.Compute(design, order, before, changed, _ => true);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That((changes[0].DisplayName, changes[0].HasValueText, changes[0].Before, changes[0].After), Is.EqualTo(("添付データ", false, "", "")));
            //作成の版でも名前だけ出る
            Assert.That(EditHistoryDiff.Compute(design, order, null, changed, _ => true).Any(e => e.FieldName == "Blob" && !e.HasValueText), Is.True);
        }

        [Test]
        public void 候補の表示名が空でも値や列挙の表示名で差分が出る()
        {
            var design = EditHistoryTestDesigns.Create();
            design.Enums.Add(new Repository.Design.EnumDesign
            {
                Name = "Policy",
                Members = { new() { Name = "Any", DisplayText = "誰か 1 人" }, new() { Name = "All", DisplayText = "全員" } },
            });
            var order = design.Modules.Find("Order")!;
            order.Fields.Add(new Repository.Design.SelectFieldDesign { Name = "Policy", DisplayName = "完了条件", DbColumn = "policy", EnumName = "Policy" });
            order.Fields.Add(new Repository.Design.SelectFieldDesign { Name = "Kind", DisplayName = "種別", DbColumn = "kind" });
            var before = Order("1", "A", 1); before.Fields["Policy"] = new SelectFieldData { Value = "Any", DisplayText = "" }; before.Fields["Kind"] = new SelectFieldData { Value = "x", DisplayText = "" };
            var after = Order("1", "A", 1); after.Fields["Policy"] = new SelectFieldData { Value = "All", DisplayText = "" }; after.Fields["Kind"] = new SelectFieldData { Value = "y", DisplayText = "" };

            var changes = EditHistoryDiff.Compute(design, order, before, after, _ => true);
            Assert.That(changes.Select(e => (e.DisplayName, e.Before, e.After)),
                Is.EqualTo(new[] { ("完了条件", "誰か 1 人", "全員"), ("種別", "x", "y") }));
        }

        [Test]
        public void 閲覧権限のない項目は差分に出ない()
        {
            var before = Order("1", "A", 100);
            var after = Order("1", "B", 100);
            after.Fields["Secret"] = new TextFieldData { Value = "changed" };
            var changes = Compute(before, after, name => name != "Secret");
            Assert.That(changes.Select(e => e.FieldName), Is.EqualTo(new[] { "Title" }));
        }

        [Test]
        public void スナップショットはファイルの中身を落として往復できる()
        {
            var data = Order("1", "A", 1);
            data.Fields["File"] = new FileFieldData { FileName = "a.txt", FileGuid = Guid.NewGuid(), Content = [1, 2, 3] };
            var json = EditHistorySnapshot.Serialize(data);
            Assert.That(json, Does.Not.Contain("Content"));
            var restored = EditHistorySnapshot.Deserialize(json)!;
            Assert.That(((FileFieldData)restored.Fields["File"]).FileName, Is.EqualTo("a.txt"));
            Assert.That(((TextFieldData)restored.Fields["Title"]).Value, Is.EqualTo("A"));
            Assert.That(restored.Fields["Items"], Is.InstanceOf<ListFieldData>());
            Assert.That(((FileFieldData)data.Fields["File"]).Content, Is.Not.Null, "元のデータは変えない");
        }
    }
}
