using ClosedXML.Excel;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.ScriptObjects
{
    //ExcelReportButtonField が使う ScriptObjects.Excel.OverWrite の検証。
    //スクリプト未実行の状態(リロード直後の一発目に相当)でも値が書き込まれることを固定する
    //(コアの ScriptRuntimeTypeManager にランタイム文脈が無いと初回だけ全セルの解決に失敗する回帰があった)
    public class ExcelReportOverWriteTest
    {
        static DesignData CreateDesignData()
        {
            var designData = new DesignData();
            var item = new ModuleDesign { Name = "Item", DataSourceName = "Test", DbTable = "item" };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            item.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            item.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "amount" });
            designData.AddModule(item);
            return designData;
        }

        static MemoryStream CreateTemplate()
        {
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).SetValue("$Name.Value");
            sheet.Cell(1, 2).SetValue("$Amount.Value");
            var stream = new MemoryStream();
            book.SaveAs(stream);
            stream.Position = 0;
            return stream;
        }

        static async Task<(string name, string amount)> RunOverWrite(TestServices services)
        {
            var data = new ModuleData { Name = "Item" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Name"] = new TextFieldData { Value = "Tanaka" };
            data.Fields["Amount"] = new NumberFieldData { Value = 123 };
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, data, ModuleLayoutType.None, "");

            using var excel = new Blazor.Extras.ScriptObjects.Excel(CreateTemplate(), "report.xlsx")
            {
                Services = services.Core,
            };
            await excel.OverWrite(module);

            using var result = new XLWorkbook(new MemoryStream(excel.GetBytes()));
            var sheet = result.Worksheet(1);
            return (sheet.Cell(1, 1).GetString(), sheet.Cell(1, 2).GetString());
        }

        [Test]
        public async Task 初回実行でも値が書き込まれる()
        {
            var services = new TestServices(CreateDesignData());

            //スクリプト未実行の初回 (リロード直後の一発目に相当)
            var first = await RunOverWrite(services);
            Assert.Multiple(() =>
            {
                Assert.That(first.name, Is.EqualTo("Tanaka"));
                Assert.That(first.amount, Is.EqualTo("123"));
            });

            //2回目も同じ
            var second = await RunOverWrite(services);
            Assert.That(second.name, Is.EqualTo("Tanaka"));
        }
    }
}
