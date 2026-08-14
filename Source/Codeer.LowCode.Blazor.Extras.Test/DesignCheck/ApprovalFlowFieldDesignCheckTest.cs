using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class ApprovalFlowFieldDesignCheckTest
    {
        static DesignData CreateDesignData(out ModuleDesign owner)
        {
            var d = new DesignData();
            owner = Utilities.CreateModule("Request");
            owner.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            d.AddModule(owner);
            return d;
        }

        static ModuleDesign CreateFlowModule(string name = "ApprovalFlow")
        {
            var flow = Utilities.CreateModule(name);
            flow.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = ApprovalFieldNames.Flow.Status, DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = ApprovalFieldNames.Flow.TargetModuleName, DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = ApprovalFieldNames.Flow.TargetId, DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = ApprovalFieldNames.Flow.RouteName, DbColumn = "DbColumn" });
            flow.Fields.Add(new NumberFieldDesign { Name = ApprovalFieldNames.Flow.AttemptNo, DbColumn = "DbColumn" });
            flow.Fields.Add(new NumberFieldDesign { Name = ApprovalFieldNames.Flow.CurrentStepNo, DbColumn = "DbColumn" });
            return flow;
        }

        [Test]
        public void モジュール不在は指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" };
            owner.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            //Flow / Member / History の3モジュールが不在
            Assert.That(ret.Count, Is.EqualTo(3));
        }

        [Test]
        public void 必須フィールドの欠落は指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" };
            owner.Fields.Add(field);

            //Status が無いフローモジュール
            var flow = CreateFlowModule();
            flow.Fields.RemoveAll(e => e.Name == ApprovalFieldNames.Flow.Status);
            d.AddModule(flow);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            var missingStatus = ret.Where(e => e.Message.Contains("'Status'")).ToList();
            Assert.That(missingStatus.Count, Is.EqualTo(1));
            missingStatus[0].AssertFieldLocation("Request", "Approval", nameof(ApprovalFlowFieldDesign.FlowModuleName));
        }

        [Test]
        public void モジュールリネームに追従する()
        {
            var d = new DesignData();
            var field = new ApprovalFlowFieldDesign
            {
                Name = "Approval",
                FlowModuleName = "ApprovalFlow",
                MemberModuleName = "ApprovalFlowMember",
                HistoryModuleName = "ApprovalHistory",
            };

            var context = new RenameContext(d)
            {
                Type = RenameType.Module,
                ModuleName = "ApprovalFlow",
                OwnerModule = "Request",
                Source = "ApprovalFlow",
                Destination = "ShareApprovalFlow",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.FlowModuleName, Is.EqualTo("ShareApprovalFlow"));
            Assert.That(field.MemberModuleName, Is.EqualTo("ApprovalFlowMember"));
        }
    }
}
