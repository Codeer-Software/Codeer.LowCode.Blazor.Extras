using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Test.Approval
{
    //経路マスタ行 → ApprovalRouteData の組み立て (LoadRoute の中身)。
    //フィールド名は契約既定名をそのまま使う (解決は契約の責務なのでここでは固定)
    public class ApprovalRouteMasterLogicTest
    {
        static readonly ApprovalRouteStepContractFieldDesign StepNames = new();
        static readonly ApprovalRouteStepMemberContractFieldDesign MemberNames = new();

        static ModuleData StepRow(string id, decimal stepNo, string name,
            string type = "", string policy = "", bool? commentRequired = null, string returnScope = "")
        {
            var d = new ModuleData();
            d.Fields[SystemFieldNames.Id] = new IdFieldData { Value = id };
            d.Fields[StepNames.StepNo] = new NumberFieldData { Value = stepNo };
            d.Fields[StepNames.StepName] = new TextFieldData { Value = name };
            d.Fields[StepNames.StepType] = new TextFieldData { Value = type };
            d.Fields[StepNames.CompletionPolicy] = new TextFieldData { Value = policy };
            d.Fields[StepNames.ReturnScope] = new TextFieldData { Value = returnScope };
            d.Fields[StepNames.IsCommentRequiredOnReject] = new BooleanFieldData { Value = commentRequired };
            return d;
        }

        static ModuleData MemberRow(string stepId, string user, bool? required = null)
        {
            var d = new ModuleData();
            d.Fields[MemberNames.Step] = new LinkFieldData { Value = stepId };
            d.Fields[MemberNames.ApproverUser] = new LinkFieldData { Value = user };
            d.Fields[MemberNames.IsRequired] = new BooleanFieldData { Value = required };
            return d;
        }

        [Test]
        public void 経路組み立て_StepNo順と既定値とメンバー分配()
        {
            var steps = new List<ModuleData>
            {
                StepRow("2", 20, "二次", policy: "Any"),
                StepRow("1", 10, "一次"),
            };
            var members = new List<ModuleData>
            {
                MemberRow("1", "A"),
                MemberRow("2", "B"),
                MemberRow("2", "C", required: false),
                MemberRow("2", ""), //承認者未選択の行はスキップ
            };

            var route = ApprovalRouteMasterLogic.Build("経理ルート", StepNames, MemberNames, steps, members);

            Assert.That(route.Name, Is.EqualTo("経理ルート"));
            Assert.That(route.Steps.Select(e => e.Name), Is.EqualTo(new[] { "一次", "二次" }));

            //既定値 (スクリプト組み立てと同じ)
            var step1 = route.Steps[0];
            Assert.That(step1.StepType, Is.EqualTo(ApprovalStepType.Approval.ToDesignValue()));
            Assert.That(step1.CompletionPolicy, Is.EqualTo(ApprovalCompletionPolicy.RequiredMembers.ToDesignValue()));
            Assert.That(step1.ReturnScope, Is.EqualTo(ApprovalReturnScope.ApplicantOnly.ToDesignValue()));
            Assert.That(step1.IsCommentRequiredOnReject, Is.True);
            Assert.That(step1.Members.Select(e => e.UserId), Is.EqualTo(new[] { "A" }));
            Assert.That(step1.Members[0].IsRequired, Is.True);

            //明示値
            var step2 = route.Steps[1];
            Assert.That(step2.CompletionPolicy, Is.EqualTo(ApprovalCompletionPolicy.Any.ToDesignValue()));
            Assert.That(step2.Members.Select(e => e.UserId), Is.EqualTo(new[] { "B", "C" }));
            Assert.That(step2.Members.Select(e => e.IsRequired), Is.EqualTo(new[] { true, false }));
        }

        [Test]
        public void 経路組み立て_シンプル形態はステップ直付け承認者()
        {
            //1ステップ1人: Members 役割を空にして ApproverUser (ステップ行の Link) を使う
            var stepNames = new ApprovalRouteStepContractFieldDesign { Members = "", ApproverUser = "Approver" };

            ModuleData SimpleStep(string id, decimal no, string name, string approver)
            {
                var d = new ModuleData();
                d.Fields[SystemFieldNames.Id] = new IdFieldData { Value = id };
                d.Fields[stepNames.StepNo] = new NumberFieldData { Value = no };
                d.Fields[stepNames.StepName] = new TextFieldData { Value = name };
                d.Fields["Approver"] = new LinkFieldData { Value = approver };
                return d;
            }

            var steps = new List<ModuleData>
            {
                SimpleStep("2", 2, "部長承認", "B"),
                SimpleStep("1", 1, "課長承認", "A"),
                SimpleStep("3", 3, "編集途中", ""), //承認者未選択はスキップ (ステップ自体は残る)
            };

            var route = ApprovalRouteMasterLogic.Build("総務ルート", stepNames, new(), steps, new List<ModuleData>());

            Assert.That(route.Steps.Select(e => e.Name), Is.EqualTo(new[] { "課長承認", "部長承認", "編集途中" }));
            Assert.That(route.Steps[0].Members.Single().UserId, Is.EqualTo("A"));
            Assert.That(route.Steps[0].Members.Single().IsRequired, Is.True);
            Assert.That(route.Steps[1].Members.Single().UserId, Is.EqualTo("B"));
            Assert.That(route.Steps[2].Members, Is.Empty);
        }

        [Test]
        public void 経路組み立て_明示値の尊重と回覧ステップ()
        {
            var steps = new List<ModuleData>
            {
                StepRow("1", 1, "回覧", type: ApprovalStepType.Confirmation.ToDesignValue(),
                    commentRequired: false, returnScope: ApprovalReturnScope.AnyPreviousStep.ToDesignValue()),
            };
            var route = ApprovalRouteMasterLogic.Build("R", StepNames, MemberNames, steps, new List<ModuleData>());

            var step = route.Steps[0];
            Assert.That(step.StepType, Is.EqualTo(ApprovalStepType.Confirmation.ToDesignValue()));
            Assert.That(step.IsCommentRequiredOnReject, Is.False);
            Assert.That(step.ReturnScope, Is.EqualTo(ApprovalReturnScope.AnyPreviousStep.ToDesignValue()));
            Assert.That(step.Members, Is.Empty);
        }
    }
}
