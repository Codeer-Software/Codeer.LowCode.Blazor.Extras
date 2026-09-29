using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 承認フロー (ApprovalFlowField) を持つモジュールの編集履歴 (クライアント側)。
    /// 承認フローの FK はサーバーの command API だけが申請書の保存とは別のタイミングで書くので、
    /// 差分に出さず、「この版に戻す」でも触らない (承認の記録は承認モジュール側の履歴)。
    /// </summary>
    public class EditHistoryApprovalFieldTest
    {
        static DesignData CreateDesign()
        {
            var design = EditHistoryTestDesigns.Create();
            design.Modules.Find("Order")!.Fields.Add(new ApprovalFlowFieldDesign { Name = "Approval", DbColumn = "approval_id" });
            return design;
        }

        static ModuleData Order(string title, string? flowId)
        {
            var data = new ModuleData { Name = "Order" };
            data.Fields["Id"] = new IdFieldData { Value = "1" };
            data.Fields["Title"] = new TextFieldData { Value = title };
            if (flowId != null) data.Fields["Approval"] = new ApprovalFlowFieldData { Id = flowId };
            return data;
        }

        [Test]
        public void 承認フローのFKは差分に出ない()
        {
            var design = CreateDesign();
            var order = design.Modules.Find("Order")!;

            //申請 (FK が付いた) の前後で件名だけ変えた版: 件名だけが出る
            var changes = EditHistoryDiff.Compute(design, order, Order("A", null), Order("B", "5"), (_, _) => true);
            Assert.That(changes.Select(e => (e.FieldName, e.Before, e.After)), Is.EqualTo(new[] { ("Title", "A", "B") }));

            //FK だけが違う版 (承認の command API が書いた後の保存): 変更なし
            changes = EditHistoryDiff.Compute(design, order, Order("A", null), Order("A", "5"), (_, _) => true);
            Assert.That(changes, Is.Empty);

            //作成の版でも FK は出ない
            changes = EditHistoryDiff.Compute(design, order, null, Order("A", "5"), (_, _) => true);
            Assert.That(changes.Select(e => e.FieldName), Is.EqualTo(new[] { "Title" }));
        }

        [Test]
        public async Task この版に戻すは承認フローのFKを触らない()
        {
            var services = new TestServices(CreateDesign());
            services.App.CurrentUserData = new ModuleData { Name = "AppUser" };
            //申請済み (FK = 5) のレコードを開いている
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, Order("B", "5"), ModuleLayoutType.None);
            var approval = module.GetField<ApprovalFlowField>("Approval")!;
            Assert.That(approval.IsSubmitted, Is.True);

            //申請前の版 (FK なし) に戻す: 件名は戻り、承認の状態はそのまま
            var applied = await EditHistoryRestorer.ApplyAsync(module, Order("A", null), null);
            Assert.That(applied, Is.EqualTo(1), "件名だけ");
            Assert.That(module.GetField<TextField>("Title")!.Value, Is.EqualTo("A"));
            Assert.That((approval.FlowId, approval.IsSubmitted), Is.EqualTo(("5", true)));

            //FK が null で入っている版 (申請前の版で NULL 列も残る) でも同じ
            var snapshot = Order("C", null);
            snapshot.Fields["Approval"] = new ApprovalFlowFieldData { Id = null };
            applied = await EditHistoryRestorer.ApplyAsync(module, snapshot, null);
            Assert.That(applied, Is.EqualTo(1));
            Assert.That((approval.FlowId, approval.IsSubmitted), Is.EqualTo(("5", true)));
        }
    }
}
