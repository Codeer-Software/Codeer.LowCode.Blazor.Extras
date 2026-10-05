using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Rename
{
    public class CrossTabFieldDesignRenameTest
    {
        //Order (Status / OrderedOn / Amount / Customer → Customer.Region) を集計する表
        static (DesignData Design, CrossTabFieldDesign Field) Create()
        {
            var d = new DesignData();
            var order = new ModuleDesign { Name = "Order" };
            order.Fields.Add(new TextFieldDesign { Name = "Status" });
            order.Fields.Add(new DateFieldDesign { Name = "OrderedOn" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount" });
            order.Fields.Add(new LinkFieldDesign { Name = "Customer", SearchCondition = new SearchCondition("Customer") });
            d.AddModule(order);
            var customer = new ModuleDesign { Name = "Customer" };
            customer.Fields.Add(new TextFieldDesign { Name = "Region" });
            d.AddModule(customer);

            var field = new CrossTabFieldDesign { Name = "Tab" };
            field.SearchCondition.ModuleName = "Order";
            field.Setting.Rows.Add(new ValueGroup { Variable = "Status.Value" });
            field.Setting.Columns.Add(new DateGroup { Variable = "OrderedOn.Value", Bucket = DateBucket.Month });
            field.Setting.Columns.Add(new ValueGroup { Variable = "Customer.Region.Value" });
            field.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Count });
            field.Setting.Measures.Add(new AggregateMeasure { Function = AggregateFunction.Sum, Variable = "Amount.Value" });
            var summary = new ModuleDesign { Name = "Summary" };
            summary.Fields.Add(field);
            d.AddModule(summary);
            return (d, field);
        }

        static RenameResult Rename(DesignData d, CrossTabFieldDesign field, string module, string source, string destination)
            => field.ChangeName(new RenameContext(d) { Type = RenameType.Field, ModuleName = module, OwnerModule = "Summary", Source = source, Destination = destination });

        [Test]
        public void 元モジュールの項目の改名に行と列と値が追従する()
        {
            var (d, field) = Create();
            foreach (var (source, destination) in new[] { ("Status", "State"), ("OrderedOn", "OrderDate"), ("Amount", "Price") })
            {
                var result = Rename(d, field, "Order", source, destination);
                Assert.That(result.RenameNeeded, source);
                result.RenameAction();
            }
            Assert.Multiple(() =>
            {
                Assert.That(field.Setting.Rows[0].Variable, Is.EqualTo("State.Value"));
                Assert.That(field.Setting.Columns[0].Variable, Is.EqualTo("OrderDate.Value"));
                //まとめ方 (DateGroup) はそのまま
                Assert.That(field.Setting.Columns[0], Is.TypeOf<DateGroup>());
                Assert.That(field.Setting.Measures[1].Variable, Is.EqualTo("Price.Value"));
                Assert.That(field.Setting.Measures[0].Variable, Is.Empty);
            });
        }

        [Test]
        public void リンク越しの項目はリンクの項目の改名にもリンク先の項目の改名にも追従する()
        {
            var (d, field) = Create();
            var result = Rename(d, field, "Order", "Customer", "Client");
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.Setting.Columns[1].Variable, Is.EqualTo("Client.Region.Value"));

            d.Modules.Find("Order")!.Fields.OfType<LinkFieldDesign>().Single().Name = "Client";
            result = Rename(d, field, "Customer", "Region", "Area");
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.Setting.Columns[1].Variable, Is.EqualTo("Client.Area.Value"));
        }

        [Test]
        public void 関係のない項目と別モジュールの同名の項目の改名では変わらない()
        {
            var (d, field) = Create();
            Assert.That(Rename(d, field, "Order", "Other", "Other2").RenameNeeded, Is.False);
            //Customer にも Status があるとしても Order の Status ではない
            Assert.That(Rename(d, field, "Customer", "Status", "State").RenameNeeded, Is.False);
            Assert.That(field.Setting.Rows[0].Variable, Is.EqualTo("Status.Value"));
        }

        [Test]
        public void 元モジュールの改名に追従する()
        {
            var (d, field) = Create();
            var result = field.ChangeName(new RenameContext(d) { Type = RenameType.Module, Source = "Order", Destination = "SalesOrder" });
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.SearchCondition.ModuleName, Is.EqualTo("SalesOrder"));
        }
    }
}
