using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 履歴の Command (受け取った操作 = ModuleSubmitData の JSON)。監査用にそのまま残すが、
    /// パスワードの平文と添付ファイルの中身は落とし、仮 Id は採番された実 Id に置き換える。
    /// </summary>
    public class EditHistoryCommandTest
    {
        [Test]
        public void 送信内容をそのまま残し_パスワードとファイルの中身だけ落とし_仮Idは実Idにする()
        {
            var tempId = "@temporary:" + Guid.NewGuid();
            var itemTempId = "@temporary:" + Guid.NewGuid();
            var order = new ModuleData { Name = "Order" };
            order.Fields["Id"] = new IdFieldData { Value = tempId };
            order.Fields["Title"] = new TextFieldData { Value = "受注A" };
            order.Fields["Secret"] = new PasswordFieldData { Value = "p@ss" };
            order.Fields["Attachment"] = new FileFieldData { FileName = "a.pdf", Content = new byte[] { 1, 2, 3 } };
            var item = new ModuleData { Name = "OrderItem" };
            item.Fields["Id"] = new IdFieldData { Value = itemTempId };
            item.Fields["Order"] = new LinkFieldData { Value = tempId };
            item.Fields["Name"] = new TextFieldData { Value = "品X" };
            var submit = new ModuleSubmitData { ModuleName = "Order", Id = tempId, Add = [order, item] };
            submit.Delete.Add(new ModuleDeleteInfo { ModuleName = "OrderItem", Id = "9" });

            var json = EditHistoryCommand.Serialize(submit, new Dictionary<string, string> { [tempId] = "1", [itemTempId] = "10" });

            //本体の型で読み戻せる (形は ModuleSubmitData そのまま)
            var back = JsonConverterEx.DeserializeObject<ModuleSubmitData>(json)!;
            Assert.That((back.ModuleName, back.Id), Is.EqualTo(("Order", "1")));
            Assert.That(back.Add.Select(e => ((IdFieldData)e.Fields["Id"]).Value), Is.EqualTo(new[] { "1", "10" }));
            Assert.That(((LinkFieldData)back.Add[1].Fields["Order"]).Value, Is.EqualTo("1"), "親へのリンクの仮 Id も実 Id");
            Assert.That(((TextFieldData)back.Add[0].Fields["Title"]).Value, Is.EqualTo("受注A"));
            Assert.That(((PasswordFieldData)back.Add[0].Fields["Secret"]).Value, Is.Null, "パスワードの平文は残さない (送られたことは残る)");
            Assert.That(((FileFieldData)back.Add[0].Fields["Attachment"]).FileName, Is.EqualTo("a.pdf"));
            Assert.That(((FileFieldData)back.Add[0].Fields["Attachment"]).Content, Is.Null, "ファイルの中身は残さない");
            Assert.That(back.Delete.Single().Id, Is.EqualTo("9"));
            Assert.That(json, Does.Not.Contain("@temporary:"));

            //元の送信は変えない (base の保存に渡すものはそのまま)
            Assert.That(((PasswordFieldData)order.Fields["Secret"]).Value, Is.EqualTo("p@ss"));
            Assert.That(((IdFieldData)order.Fields["Id"]).Value, Is.EqualTo(tempId));
        }

        [Test]
        public async Task 版は履歴行のCommandを持ち_役割が無ければ空()
        {
            var design = EditHistoryTestDesigns.Create();
            var services = new TestServices(design);
            services.App.CurrentUserData = new ModuleData { Name = "AppUser" };
            var snapshot = new ModuleData { Name = "Order" };
            snapshot.Fields["Id"] = new IdFieldData { Value = "1" };
            snapshot.Fields["Title"] = new TextFieldData { Value = "A" };
            var row = new ModuleData { Name = "EditHistory" };
            row.Fields["Id"] = new IdFieldData { Value = "101" };
            row.Fields["ChangeType"] = new TextFieldData { Value = "Add" };
            row.Fields["Snapshot"] = new TextFieldData { Value = EditHistorySnapshot.Serialize(snapshot) };
            row.Fields["Command"] = new TextFieldData { Value = "{\"ModuleName\":\"Order\"}" };
            services.App.ListProvider = _ => new Paging<ModuleData> { TotalCount = 1, Items = [row] };

            var module = await ModuleCreationService.CreateModuleAsync(services.Core, snapshot, ModuleLayoutType.Detail);
            Assert.That(module.GetField<EditHistoryField>("History")!.Versions.Single().Command, Is.EqualTo("{\"ModuleName\":\"Order\"}"));
            Assert.That(services.App.ListRequests[0].Condition.SelectFields, Does.Contain("Command"), "履歴の読み出しで Command 列も要求する");

            //契約に Command 役割が無い履歴モジュール (既存の設計) では空
            design.Modules.Find("EditHistory")!.Fields.OfType<Designs.EditHistoryContractFieldDesign>().Single().Command = string.Empty;
            services.App.ListRequests.Clear();
            module = await ModuleCreationService.CreateModuleAsync(services.Core, snapshot, ModuleLayoutType.Detail);
            Assert.That(module.GetField<EditHistoryField>("History")!.Versions.Single().Command, Is.Empty);
            Assert.That(services.App.ListRequests[0].Condition.SelectFields, Does.Not.Contain("Command"));
        }
    }
}
