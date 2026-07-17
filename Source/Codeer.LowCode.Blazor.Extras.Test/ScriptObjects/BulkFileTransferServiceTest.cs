using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.ScriptObjects
{
    //一括ダウンロード (BulkFileTransferService / BulkFileTransferButtonField 共有ロジック) の
    //条件解決を、コードで組んだデザインから作った実 Module 文脈で確認する
    public class BulkFileTransferServiceTest
    {
        static DesignData CreateDesignData()
        {
            var designData = new DesignData();

            var orders = new ModuleDesign { Name = "Orders", DataSourceName = "Test", DbTable = "orders" };
            orders.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            orders.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "status" });
            designData.AddModule(orders);

            var mod = new ModuleDesign { Name = "mod" };
            mod.Fields.Add(new ListFieldDesign
            {
                Name = "OrderList",
                SearchCondition = new SearchCondition("Orders") { LimitCount = 10 },
            });
            mod.Fields.Add(new SearchFieldDesign { Name = "OrderSearch", ResultsViewFieldName = "OrderList" });
            designData.AddModule(mod);

            return designData;
        }

        [Test]
        public async Task DownloadByListField()
        {
            var services = new TestServices(CreateDesignData());
            var mod = await services.CreateModuleAsync("mod");
            var listField = (ListField)mod.GetField("OrderList")!;

            var target = new BulkFileTransferService { Services = services.Core };
            await target.DownloadAsync(listField);

            //リストフィールドの検索条件で list_file が呼ばれ、ファイルがダウンロードされる
            var condition = services.App.LastListFileCondition;
            Assert.That(condition, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(condition!.ModuleName, Is.EqualTo("Orders"));
                //一覧レイアウトの列には絞らない (全列ダウンロード)
                Assert.That(condition.SelectFields, Is.Empty);
                //一覧のページサイズ (デザインでは10) には縛られない (全件ダウンロード)
                Assert.That(condition.LimitCount, Is.Null);
                //ファイル名は {モジュール名}.{拡張子} (IBulkFileTransferFieldDesign 未定義なら xlsx)
                Assert.That(((DummyUIService)services.Core.UIService).Downloads.Single().Name, Is.EqualTo("Orders.xlsx"));
            });
        }

        [Test]
        public async Task DownloadBySearchField()
        {
            var services = new TestServices(CreateDesignData());
            var mod = await services.CreateModuleAsync("mod");
            var searchField = (SearchField)mod.GetField("OrderSearch")!;

            var target = new BulkFileTransferService { Services = services.Core };
            await target.DownloadAsync(searchField);

            //未検索でも結果表示フィールド (OrderList) から対象モジュールを解決して全件ダウンロードになる
            var condition = services.App.LastListFileCondition;
            Assert.That(condition, Is.Not.Null);
            Assert.That(condition!.ModuleName, Is.EqualTo("Orders"));
        }

        [Test]
        public async Task CsvFileFormatFieldChangesDownloadExtension()
        {
            var designData = CreateDesignData();
            //対象モジュールに CsvFileFormatField を定義するとダウンロード拡張子が csv になる
            designData.Modules.Find("Orders")!.Fields.Add(new Designs.CsvFileFormatFieldDesign { Name = "CsvTransfer" });

            var services = new TestServices(designData);
            var mod = await services.CreateModuleAsync("mod");
            var listField = (ListField)mod.GetField("OrderList")!;

            var target = new BulkFileTransferService { Services = services.Core };
            await target.DownloadAsync(listField);

            Assert.That(((DummyUIService)services.Core.UIService).Downloads.Single().Name, Is.EqualTo("Orders.csv"));
        }
    }
}
