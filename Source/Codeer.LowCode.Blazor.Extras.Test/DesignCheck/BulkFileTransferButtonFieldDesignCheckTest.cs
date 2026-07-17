using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class BulkFileTransferButtonFieldDesignCheckTest
    {
        const string ConditionSourceInvalidMessage =
            "検索フィールド名とリストフィールド名は、どちらか一方だけを設定してください。";

        static (DesignData, ModuleDesign) CreateDesignDataWithSources()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var orders = Utilities.CreateModule("Orders");
            designData.AddModule(orders);
            module.Fields.Add(new ListFieldDesign
            {
                Name = "OrderList",
                SearchCondition = new SearchCondition("Orders")
            });
            module.Fields.Add(new SearchFieldDesign { Name = "OrderSearch", ResultsViewFieldName = "OrderList" });
            return (designData, module);
        }

        [Test]
        public void SuccessSearchField()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var field = new BulkFileTransferButtonFieldDesign { Name = "Transfer", SearchFieldName = "OrderSearch" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void SuccessListField()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var field = new BulkFileTransferButtonFieldDesign { Name = "Transfer", ListFieldName = "OrderList" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(0));
        }

        [Test]
        public void NoConditionSource()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var field = new BulkFileTransferButtonFieldDesign { Name = "Transfer" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo(ConditionSourceInvalidMessage));
            ret[0].AssertFieldLocation("mod", "Transfer", string.Empty);
        }

        [Test]
        public void BothConditionSources()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var field = new BulkFileTransferButtonFieldDesign
            {
                Name = "Transfer",
                SearchFieldName = "OrderSearch",
                ListFieldName = "OrderList"
            };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo(ConditionSourceInvalidMessage));
        }

        [Test]
        public void NoMatchSearchField()
        {
            var (designData, module) = CreateDesignDataWithSources();
            var field = new BulkFileTransferButtonFieldDesign { Name = "Transfer", SearchFieldName = "Nothing" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Is.EqualTo("フィールド 'Nothing' がモジュール 'mod' に存在しません。"));
            ret[0].AssertFieldLocation("mod", "Transfer", "SearchFieldName");
        }

        [Test]
        public void SearchFieldTypeMismatch()
        {
            var (designData, module) = CreateDesignDataWithSources();
            //リストフィールドを検索フィールドとして指定
            var field = new BulkFileTransferButtonFieldDesign { Name = "Transfer", SearchFieldName = "OrderList" };
            module.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("mod", "Transfer", "SearchFieldName");
        }
    }
}
