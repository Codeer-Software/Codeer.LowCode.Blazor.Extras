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

        //必須役割が空ならエラー / 必須以外は空でもエラーにしない (空 = 使わない)
        [Test]
        public void 必須役割が空なら指摘される()
        {
            var d = CreateDesignData(out _);
            var flow = CreateFlowModule();
            d.AddModule(flow);
            d.AddModule(CreateMemberModule());
            d.AddModule(CreateHistoryModule());

            var contract = flow.Fields.OfType<ApprovalFlowContractFieldDesign>().First();
            contract.Status = string.Empty;
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlow", d, Utilities.CreateDataSource()));

            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(ApprovalFlowContractFieldDesign.Status)));
            ret[0].AssertFieldLocation("ApprovalFlow", "Contract", nameof(ApprovalFlowContractFieldDesign.Status));
        }

        [Test]
        public void 必須でない役割は空でも指摘されない()
        {
            //承認契約で任意なのはメンバー契約の TurnNotifyMail だけ (空 = 通知しない)
            var d = CreateDesignData(out _);
            d.AddModule(CreateFlowModule());
            var member = CreateMemberModule();
            d.AddModule(member);
            d.AddModule(CreateHistoryModule());

            var contract = member.Fields.OfType<ApprovalMemberContractFieldDesign>().First();
            contract.TurnNotifyMail = string.Empty;
            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlowMember", d, Utilities.CreateDataSource()));

            //他の役割のフィールドが無い指摘は出るが、TurnNotifyMail についての指摘は無い
            Assert.That(ret.Select(e => e.Message), Has.None.Contains(nameof(ApprovalMemberContractFieldDesign.TurnNotifyMail)));
        }

        [Test]
        public void メンバー契約の任意役割を全て空にしても指摘なし()
        {
            //必須 = Flow / AttemptNo / StepNo / StepType / ApproverUser / Status (エンジンが状態遷移に使う)。
            //それ以外は空 = 使わない (ポリシー系は ApprovalMemberDefaults の既定で動く)
            var d = CreateDesignData(out _);
            d.AddModule(CreateFlowModule());
            var member = CreateMemberModule();
            d.AddModule(member);
            d.AddModule(CreateHistoryModule());

            var contract = member.Fields.OfType<ApprovalMemberContractFieldDesign>().First();
            var optional = new[]
            {
                nameof(contract.StepName), nameof(contract.StepType), nameof(contract.CompletionPolicy), nameof(contract.IsCommentRequiredOnReject),
                nameof(contract.ReturnScope), nameof(contract.IsRequired), nameof(contract.IsFinalStep), nameof(contract.ActedAt),
                nameof(contract.TurnNotifyMail),
            };
            foreach (var role in optional)
            {
                Assert.That(contract.IsRoleRequired(role), Is.False, role);
                typeof(ApprovalMemberContractFieldDesign).GetProperty(role)!.SetValue(contract, string.Empty);
            }
            //必須役割のフィールドだけ実装する
            member.Fields.Add(new LinkFieldDesign { Name = "Flow", DbColumn = "flow" });
            member.Fields.Add(new NumberFieldDesign { Name = "AttemptNo", DbColumn = "attempt_no" });
            member.Fields.Add(new NumberFieldDesign { Name = "StepNo", DbColumn = "step_no" });
            member.Fields.Add(new LinkFieldDesign { Name = "ApproverUser", DbColumn = "approver_user" });
            member.Fields.Add(new SelectFieldDesign { Name = "Status", DbColumn = "status" });

            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalFlowMember", d, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty, string.Join(" / ", ret.Select(e => e.Message)));
        }

        [Test]
        public void 履歴契約は_Flow_以外を全て空にしても指摘なし()
        {
            //エンジンは履歴を書くだけ・UI は表示するだけ。履歴行を見つける Flow だけが必須
            var d = CreateDesignData(out _);
            d.AddModule(CreateFlowModule());
            d.AddModule(CreateMemberModule());
            var history = CreateHistoryModule();
            d.AddModule(history);

            var contract = history.Fields.OfType<ApprovalHistoryContractFieldDesign>().First();
            foreach (var role in new[] { nameof(contract.AttemptNo), nameof(contract.Action), nameof(contract.ActorUser), nameof(contract.Comment), nameof(contract.ActedAt) })
            {
                Assert.That(contract.IsRoleRequired(role), Is.False, role);
                typeof(ApprovalHistoryContractFieldDesign).GetProperty(role)!.SetValue(contract, string.Empty);
            }
            Assert.That(contract.IsRoleRequired(nameof(contract.Flow)), Is.True);
            history.Fields.Add(new LinkFieldDesign { Name = "Flow", DbColumn = "flow" });

            var ret = contract.CheckDesign(new DesignCheckContext("ApprovalHistory", d, Utilities.CreateDataSource()));
            Assert.That(ret, Is.Empty, string.Join(" / ", ret.Select(e => e.Message)));
        }

        [Test]
        public void 必須と任意の役割はメール契約と同じく表示名の必須印と一致する()
        {
            //表示名 "(必須)" の有無 = IsRoleRequired (どちらか片方だけ直すのを防ぐ)
            foreach (var contract in new ContractFieldDesignBase[]
                     { new ApprovalFlowContractFieldDesign(), new ApprovalMemberContractFieldDesign(), new ApprovalHistoryContractFieldDesign() })
            {
                foreach (var role in contract.GetType().GetProperties()
                             .Where(e => e.PropertyType == typeof(string) && e.DeclaringType == contract.GetType()))
                {
                    var attr = (DesignerAttribute)role.GetCustomAttributes(typeof(DesignerAttribute), true).Single();
                    var display = Extras.Properties.Resources.ResourceManager.GetString(attr.DisplayName[1..],
                        new System.Globalization.CultureInfo("ja-JP"))!;
                    Assert.That(display.Contains("(必須)"), Is.EqualTo(contract.IsRoleRequired(role.Name)),
                        $"{contract.GetType().Name}.{role.Name}: {display}");
                }
            }
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
