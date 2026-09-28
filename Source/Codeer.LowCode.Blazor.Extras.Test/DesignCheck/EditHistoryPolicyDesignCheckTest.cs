using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Test.EditHistory;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    /// <summary>
    /// 従属レコードの扱い (EditHistoryField の ExcludedOwnedRecords / IndividuallyRecordedOwnedRecords) の設計チェックと、版の読み込み時の取り除き。
    /// </summary>
    public class EditHistoryPolicyDesignCheckTest
    {
        static List<DesignCheckInfo> Check(DesignData d, string module, FieldDesignBase field)
            => field.CheckDesign(new DesignCheckContext(module, d, Utilities.CreateDataSource()));

        static ModuleDesign Module(DesignData d, string name) => d.Modules.Find(name)!;
        static T Field<T>(DesignData d, string module, string name) where T : FieldDesignBase
            => (T)Module(d, module).Fields.First(e => e.Name == name);
        static EditHistoryFieldDesign History(DesignData d) => Field<EditHistoryFieldDesign>(d, "Order", "History");

        [Test]
        public void 存在しないパスは指摘_孫や埋め込みの中はドット区切りで指せる()
        {
            var d = EditHistoryTestDesigns.Create(withCustomer: true, withDetails: true);
            History(d).ExcludedOwnedRecords.Add("Nothing");
            History(d).IndividuallyRecordedOwnedRecords.Add("Items.Nothing");
            var ret = Check(d, "Order", History(d));
            Assert.That(ret.Select(e => e.Code), Is.All.EqualTo(DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 5)));
            Assert.That(ret.Count, Is.EqualTo(2));
            ret[0].AssertFieldLocation("Order", "History", nameof(EditHistoryFieldDesign.ExcludedOwnedRecords));
            ret[1].AssertFieldLocation("Order", "History", nameof(EditHistoryFieldDesign.IndividuallyRecordedOwnedRecords));

            History(d).ExcludedOwnedRecords.Clear();
            History(d).IndividuallyRecordedOwnedRecords.Clear();
            History(d).ExcludedOwnedRecords.Add("Items.Details");
            History(d).ExcludedOwnedRecords.Add("Customer");
            Assert.That(Check(d, "Order", History(d)), Is.Empty);
        }

        [Test]
        public void 行ごとに記録する先のモジュールにEditHistoryFieldが無ければ指摘()
        {
            var d = EditHistoryTestDesigns.Create();
            History(d).IndividuallyRecordedOwnedRecords.Add("Items");
            var ret = Check(d, "Order", History(d));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo(DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 6)));
            Assert.That(ret[0].Message, Does.Contain("OrderItem"));

            Module(d, "OrderItem").Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            Assert.That(Check(d, "Order", History(d)), Is.Empty);
        }

        [Test]
        public void 除外や行ごとの一覧はサーバーページングでも指摘しない()
        {
            var d = EditHistoryTestDesigns.Create();
            Module(d, "OrderItem").Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });
            Field<ListFieldDesign>(d, "Order", "Items").SearchCondition.LimitCount = 10;
            Assert.That(Check(d, "Order", History(d)).Single().Code, Is.EqualTo(DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 4)));

            History(d).ExcludedOwnedRecords.Add("Items");
            Assert.That(Check(d, "Order", History(d)), Is.Empty);

            History(d).ExcludedOwnedRecords.Clear();
            History(d).IndividuallyRecordedOwnedRecords.Add("Items");
            Assert.That(Check(d, "Order", History(d)), Is.Empty);
        }

        [Test]
        public void 版の読み込みでは含めない従属レコードを取り除く_孫はパスで()
        {
            var d = EditHistoryTestDesigns.Create(withDetails: true);
            var history = History(d);
            history.ExcludedOwnedRecords.Add("Items.Details");

            var detail = new ModuleData { Name = "OrderItemDetail" };
            detail.Fields["Id"] = new IdFieldData { Value = "100" };
            var item = new ModuleData { Name = "OrderItem" };
            item.Fields["Id"] = new IdFieldData { Value = "10" };
            item.Fields["Details"] = new ListFieldData { Children = [detail] };
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = "1" };
            order.Fields["Items"] = new ListFieldData { Children = [item] };

            var stripped = EditHistoryPolicy.Strip(d, history, order)!;
            var rows = ((ListFieldData)stripped.Fields["Items"]).Children;
            Assert.That(rows.Count, Is.EqualTo(1), "含める一覧は残る");
            Assert.That(rows[0].Fields.ContainsKey("Details"), Is.False, "除外した孫の一覧は消える");

            history.ExcludedOwnedRecords.Clear();
            history.IndividuallyRecordedOwnedRecords.Add("Items");
            Assert.That(EditHistoryPolicy.Strip(d, history, order)!.Fields.ContainsKey("Items"), Is.False, "行ごとの一覧は親の版から消える");
        }
    }
}
