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

        static ModuleDesign CreateRouteModule(string name = "ApprovalRoute", string stepModuleName = "ApprovalRouteStep")
        {
            var route = Utilities.CreateModule(name);
            route.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            route.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalRouteContractFieldDesign.RouteName), DbColumn = "DbColumn" });
            route.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalRouteContractFieldDesign.Steps),
                SearchCondition = new Repository.Match.SearchCondition { ModuleName = stepModuleName },
            });
            route.Fields.Add(new ApprovalRouteContractFieldDesign { Name = "Contract" });
            return route;
        }

        static ModuleDesign CreateRouteStepModule(string name = "ApprovalRouteStep", string memberModuleName = "ApprovalRouteStepMember")
        {
            var step = Utilities.CreateModule(name);
            step.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            step.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.Route), DbColumn = "DbColumn" });
            step.Fields.Add(new NumberFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.StepNo), DbColumn = "DbColumn" });
            step.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.StepName), DbColumn = "DbColumn" });
            step.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.StepType), DbColumn = "DbColumn" });
            step.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.CompletionPolicy), DbColumn = "DbColumn" });
            step.Fields.Add(new BooleanFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.IsCommentRequiredOnReject), DbColumn = "DbColumn" });
            step.Fields.Add(new TextFieldDesign { Name = nameof(ApprovalRouteStepContractFieldDesign.ReturnScope), DbColumn = "DbColumn" });
            step.Fields.Add(new ListFieldDesign
            {
                Name = nameof(ApprovalRouteStepContractFieldDesign.Members),
                SearchCondition = new Repository.Match.SearchCondition { ModuleName = memberModuleName },
            });
            step.Fields.Add(new ApprovalRouteStepContractFieldDesign { Name = "Contract" });
            return step;
        }

        static ModuleDesign CreateRouteStepMemberModule(string name = "ApprovalRouteStepMember")
        {
            var member = Utilities.CreateModule(name);
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            member.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalRouteStepMemberContractFieldDesign.Step), DbColumn = "DbColumn" });
            member.Fields.Add(new LinkFieldDesign { Name = nameof(ApprovalRouteStepMemberContractFieldDesign.ApproverUser), DbColumn = "DbColumn" });
            member.Fields.Add(new BooleanFieldDesign { Name = nameof(ApprovalRouteStepMemberContractFieldDesign.IsRequired), DbColumn = "DbColumn" });
            member.Fields.Add(new ApprovalRouteStepMemberContractFieldDesign { Name = "Contract" });
            return member;
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
        public void 経路マスタ指定で契約が無ければ指摘される()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn", RouteModuleName = "ApprovalRoute" };
            owner.Fields.Add(field);
            d.AddModule(CreateFlowModule());
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var route = CreateRouteModule();
            route.Fields.RemoveAll(e => e is ApprovalRouteContractFieldDesign);
            d.AddModule(route);
            d.AddModule(CreateRouteStepModule());
            d.AddModule(CreateRouteStepMemberModule());

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(ApprovalRouteContractFieldDesign)));
            ret[0].AssertFieldLocation("Request", "Approval", nameof(ApprovalFlowFieldDesign.RouteModuleName));
        }

        [Test]
        public void 経路マスタの契約が揃っていれば指摘なし()
        {
            var d = CreateDesignData(out var owner);
            var field = new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "DbColumn", RouteModuleName = "ApprovalRoute" };
            owner.Fields.Add(field);
            d.AddModule(CreateFlowModule());
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());
            d.AddModule(CreateRouteModule());
            d.AddModule(CreateRouteStepModule());
            d.AddModule(CreateRouteStepMemberModule());

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty);

            //契約フィールド側のチェックも全部通ること (役割フィールド存在 + 一覧役割の連鎖)
            var routeContract = d.Modules.Find("ApprovalRoute")!.Fields.OfType<ApprovalRouteContractFieldDesign>().First();
            Assert.That(routeContract.CheckDesign(new DesignCheckContext("ApprovalRoute", d, Utilities.CreateDataSource())), Is.Empty);
            var stepContract = d.Modules.Find("ApprovalRouteStep")!.Fields.OfType<ApprovalRouteStepContractFieldDesign>().First();
            Assert.That(stepContract.CheckDesign(new DesignCheckContext("ApprovalRouteStep", d, Utilities.CreateDataSource())), Is.Empty);
            var memberContract = d.Modules.Find("ApprovalRouteStepMember")!.Fields.OfType<ApprovalRouteStepMemberContractFieldDesign>().First();
            Assert.That(memberContract.CheckDesign(new DesignCheckContext("ApprovalRouteStepMember", d, Utilities.CreateDataSource())), Is.Empty);
        }

        [Test]
        public void 経路マスタのシンプル形態_ステップ直付け承認者で指摘なし()
        {
            //役割を空にすると「使わない」宣言。Members を空にして ApproverUser 直付けの1人構成にする
            var d = CreateDesignData(out _);
            var step = Utilities.CreateModule("SimpleRouteStep");
            step.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "DbColumn" });
            step.Fields.Add(new LinkFieldDesign { Name = "Route", DbColumn = "DbColumn" });
            step.Fields.Add(new NumberFieldDesign { Name = "StepNo", DbColumn = "DbColumn" });
            step.Fields.Add(new TextFieldDesign { Name = "StepName", DbColumn = "DbColumn" });
            step.Fields.Add(new LinkFieldDesign { Name = "Approver", DbColumn = "DbColumn" });
            var contract = new ApprovalRouteStepContractFieldDesign
            {
                Name = "Contract",
                Members = "",
                ApproverUser = "Approver",
                StepType = "",
                CompletionPolicy = "",
                IsCommentRequiredOnReject = "",
                ReturnScope = "",
            };
            step.Fields.Add(contract);
            d.AddModule(step);

            var ret = contract.CheckDesign(new DesignCheckContext("SimpleRouteStep", d, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty);

            //Members も ApproverUser も空は指摘される
            contract.ApproverUser = "";
            var ret2 = contract.CheckDesign(new DesignCheckContext("SimpleRouteStep", d, Utilities.CreateDataSource()));
            Assert.That(ret2.Count, Is.EqualTo(1));
            Assert.That(ret2[0].Message, Does.Contain("ApproverUser"));
        }

        [Test]
        public void 経路マスタのステップ一覧の先に契約が無ければ指摘される()
        {
            var d = CreateDesignData(out _);
            d.AddModule(CreateRouteModule());
            var step = CreateRouteStepModule();
            step.Fields.RemoveAll(e => e is ApprovalRouteStepContractFieldDesign);
            d.AddModule(step);
            d.AddModule(CreateRouteStepMemberModule());

            var routeContract = d.Modules.Find("ApprovalRoute")!.Fields.OfType<ApprovalRouteContractFieldDesign>().First();
            var ret = routeContract.CheckDesign(new DesignCheckContext("ApprovalRoute", d, Utilities.CreateDataSource()));

            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(ApprovalRouteStepContractFieldDesign)));
        }

        [Test]
        public void フローフィールドは経路マスタのモジュールリネームに追従する()
        {
            var d = new DesignData();
            var field = new ApprovalFlowFieldDesign { Name = "Approval", RouteModuleName = "ApprovalRoute" };

            var context = new RenameContext(d)
            {
                Type = RenameType.Module,
                ModuleName = "ApprovalRoute",
                OwnerModule = "Request",
                Source = "ApprovalRoute",
                Destination = "SharedRoute",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.RouteModuleName, Is.EqualTo("SharedRoute"));
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
