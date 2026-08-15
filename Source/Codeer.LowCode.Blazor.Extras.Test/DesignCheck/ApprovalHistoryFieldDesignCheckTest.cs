using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class ApprovalHistoryFieldDesignCheckTest
    {
        static DesignData CreateDesignData(out ModuleDesign owner)
        {
            var d = new DesignData();
            owner = Utilities.CreateModule("Request");
            owner.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            d.AddModule(owner);
            return d;
        }

        [Test]
        public void 承認フィールド不在は指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalHistoryFieldDesign { Name = "History", ApprovalFieldName = "Approval" };
            owner.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("Request", "History", nameof(ApprovalHistoryFieldDesign.ApprovalFieldName));
        }

        [Test]
        public void 参照先が承認フローフィールドでなければ指摘される()
        {
            var d = CreateDesignData(out var owner);
            owner.Fields.Add(new TextFieldDesign { Name = "Approval", DbColumn = "DbColumn" });
            var field = new ApprovalHistoryFieldDesign { Name = "History", ApprovalFieldName = "Approval" };
            owner.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("Request", "History", nameof(ApprovalHistoryFieldDesign.ApprovalFieldName));
        }

        [Test]
        public void 承認フローフィールドを参照していれば指摘なし()
        {
            var d = CreateDesignData(out var owner);
            owner.Fields.Add(new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" });
            var field = new ApprovalHistoryFieldDesign { Name = "History", ApprovalFieldName = "Approval" };
            owner.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void 承認フィールドのリネームに追従する()
        {
            var d = new DesignData();
            var field = new ApprovalHistoryFieldDesign { Name = "History", ApprovalFieldName = "Approval" };

            var context = new RenameContext(d)
            {
                Type = RenameType.Field,
                ModuleName = "Request",
                OwnerModule = "Request",
                Source = "Approval",
                Destination = "Sign",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.ApprovalFieldName, Is.EqualTo("Sign"));
        }
    }
}
