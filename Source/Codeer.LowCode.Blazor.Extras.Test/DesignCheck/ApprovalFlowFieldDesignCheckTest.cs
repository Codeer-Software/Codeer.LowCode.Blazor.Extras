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

        static ModuleDesign CreateFlowModule(string name = "ApprovalFlow",
            string memberModuleName = "ApprovalFlowMember", string historyModuleName = "ApprovalHistory")
        {
            var flow = Utilities.CreateModule(name);
            flow.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.Status), DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.TargetModuleName), DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.TargetId), DbColumn = "DbColumn" });
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.RouteName), DbColumn = "DbColumn" });
            flow.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.Applicant), DbColumn = "DbColumn" });
            flow.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.AttemptNo), DbColumn = "DbColumn" });
            flow.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.CurrentStepNo), DbColumn = "DbColumn" });
            flow.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalFlowContractFieldDesign.Members),
                SearchCondition = new Repository.Match.SearchCondition { ModuleName = memberModuleName },
            });
            flow.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalFlowContractFieldDesign.Histories),
                SearchCondition = new Repository.Match.SearchCondition { ModuleName = historyModuleName },
            });
            flow.Fields.Add(new ApprovalFlowContractFieldDesign { Name = "Contract" });
            return flow;
        }

        static ModuleDesign CreateMemberModule(string name = "ApprovalFlowMember")
        {
            var module = Utilities.CreateModule(name);
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            module.Fields.Add(new ApprovalMemberContractFieldDesign { Name = "Contract" });
            return module;
        }

        static ModuleDesign CreateHistoryModule(string name = "ApprovalHistory")
        {
            var module = Utilities.CreateModule(name);
            module.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            module.Fields.Add(new ApprovalHistoryContractFieldDesign { Name = "Contract" });
            return module;
        }

        [Test]
        public void フローモジュール不在は指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" };
            owner.Fields.Add(field);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("Request", "Approval", nameof(ApprovalFlowFieldDesign.FlowModuleName));
        }

        [Test]
        public void フローモジュールに契約フィールドが無ければ指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" };
            owner.Fields.Add(field);

            var flow = CreateFlowModule();
            flow.Fields.RemoveAll(e => e is ApprovalFlowContractFieldDesign);
            d.AddModule(flow);

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(ApprovalFlowContractFieldDesign)));
            ret[0].AssertFieldLocation("Request", "Approval", nameof(ApprovalFlowFieldDesign.FlowModuleName));
        }

        [Test]
        public void 契約を実装していれば指摘なし()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn" };
            owner.Fields.Add(field);
            d.AddModule(CreateFlowModule());
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty);

            var flow = d.Modules.Find("ApprovalFlow")!;
            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            var contractRet = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));
            Assert.That(contractRet, Is.Empty);
        }

        [Test]
        public void 役割フィールドの欠落は契約フィールドが指摘する()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            flow.Fields.RemoveAll(e => e.Name == nameof(ApprovalFlowContractFieldDesign.Status));
            d.AddModule(flow);
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("ApprovalFlow", "Contract", nameof(ApprovalFlowContractFieldDesign.Status));
        }

        [Test]
        public void 役割フィールドをリネームしてマッピングを追従させれば通る()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            flow.Fields.First(e => e.Name == nameof(ApprovalFlowContractFieldDesign.Status)).Name = "State";
            d.AddModule(flow);
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            contract.Status = "State";
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void Members役割が一覧フィールドでなければ指摘される()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            flow.Fields.RemoveAll(e => e.Name == nameof(ApprovalFlowContractFieldDesign.Members));
            flow.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalFlowContractFieldDesign.Members), DbColumn = "DbColumn" });
            d.AddModule(flow);
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("ApprovalFlow", "Contract", nameof(ApprovalFlowContractFieldDesign.Members));
        }

        [Test]
        public void Members一覧の先のモジュールに契約フィールドが無ければ指摘される()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            d.AddModule(flow);
            var member = CreateMemberModule();
            member.Fields.RemoveAll(e => e is ApprovalMemberContractFieldDesign);
            d.AddModule(member);
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(ApprovalMemberContractFieldDesign)));
            ret[0].AssertFieldLocation("ApprovalFlow", "Contract", nameof(ApprovalFlowContractFieldDesign.Members));
        }

        [Test]
        public void 契約フィールドの重複は指摘される()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            flow.Fields.Add(new ApprovalFlowContractFieldDesign { Name = "Contract2" });
            d.AddModule(flow);
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            var duplicated = ret.Where(e => e.Message.Contains(nameof(ApprovalFlowContractFieldDesign))).ToList();
            Assert.That(duplicated.Count, Is.EqualTo(1));
        }

        [Test]
        public void フローフィールドはモジュールリネームに追従する()
        {
            var d = new DesignData();
            var field = new ApprovalFlowFieldDesign
            {
                Name = "Approval",
                FlowModuleName = "ApprovalFlow",
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
        }

        [Test]
        public void 契約フィールドは役割のフィールドリネームに追従する()
        {
            var d = new DesignData();
            var contract = new ApprovalFlowContractFieldDesign { Name = "Contract" };

            var context = new RenameContext(d)
            {
                Type = RenameType.Field,
                ModuleName = "ApprovalFlow",
                OwnerModule = "ApprovalFlow",
                Source = nameof(ApprovalFlowContractFieldDesign.Status),
                Destination = "State",
            };
            var result = contract.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(contract.Status, Is.EqualTo("State"));
            Assert.That(contract.TargetId, Is.EqualTo(nameof(ApprovalFlowContractFieldDesign.TargetId)));
        }
    }
}
