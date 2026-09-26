using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Test.EditHistory;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class EditHistoryDesignCheckTest
    {
        static List<DesignCheckInfo> Check(DesignData d, string module, FieldDesignBase field)
            => field.CheckDesign(new DesignCheckContext(module, d, Utilities.CreateDataSource()));

        static DesignData Create() => EditHistoryTestDesigns.Create();
        static ModuleDesign Module(DesignData d, string name) => d.Modules.Find(name)!;
        static T Field<T>(DesignData d, string module, string name) where T : FieldDesignBase
            => (T)Module(d, module).Fields.First(e => e.Name == name);

        [Test]
        public void 契約を満たしていれば指摘なし()
        {
            var d = Create();
            Assert.That(Check(d, "Order", Field<EditHistoryFieldDesign>(d, "Order", "History")), Is.Empty);
            Assert.That(Check(d, "EditHistory", Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract")), Is.Empty);
        }

        [Test]
        public void 履歴モジュールに契約フィールドが無ければ指摘()
        {
            var d = Create();
            Module(d, "EditHistory").Fields.RemoveAll(e => e is EditHistoryContractFieldDesign);
            var ret = Check(d, "Order", Field<EditHistoryFieldDesign>(d, "Order", "History"));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(EditHistoryContractFieldDesign)));
            ret[0].AssertFieldLocation("Order", "History", nameof(EditHistoryFieldDesign.HistoryModuleName));
        }

        [Test]
        public void 履歴モジュールが存在しなければ指摘()
        {
            var d = EditHistoryTestDesigns.Create(historyModuleName: "Nothing");
            var ret = Check(d, "Order", Field<EditHistoryFieldDesign>(d, "Order", "History"));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("Order", "History", nameof(EditHistoryFieldDesign.HistoryModuleName));
        }

        [Test]
        public void 同じモジュールに2つ置くと指摘()
        {
            var d = Create();
            Module(d, "Order").Fields.Add(new EditHistoryFieldDesign { Name = "History2" });
            var ret = Check(d, "Order", Field<EditHistoryFieldDesign>(d, "Order", "History"));
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("Order", "History", nameof(FieldDesignBase.Name));
        }

        [Test]
        public void 対象モジュール名はSelectでもよく_enumにメンバーがあるのに記録元が無ければ指摘_enumが無いか空なら指摘しない()
        {
            var d = Create();
            var history = Module(d, "EditHistory");
            history.Fields.RemoveAll(e => e.Name == "ModuleName");
            history.Fields.Add(new SelectFieldDesign { Name = "ModuleName", DbColumn = "module_name", EnumName = "EditHistoryTargetModule" });
            var contract = Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract");
            //enum が無い = 読み替え無しで運用
            Assert.That(Check(d, "EditHistory", contract), Is.Empty);
            var targets = new EnumDesign { Name = "EditHistoryTargetModule" };
            d.Enums.Add(targets);
            //空でも同じ
            Assert.That(Check(d, "EditHistory", contract), Is.Empty);
            //メンバーを持つのに記録元 (Order) のメンバーが無い
            targets.Members.Add(new EnumMemberDesign { Name = "Other", DisplayText = "その他" });
            var ret = Check(d, "EditHistory", contract);
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain("Order").And.Contain("EditHistoryTargetModule"));
            ret[0].AssertFieldLocation("EditHistory", "Contract", nameof(EditHistoryContractFieldDesign.ModuleName));
            //足せば消える (値 = モジュール名)
            targets.Members.Add(new EnumMemberDesign { Name = "Order", DisplayText = "受注" });
            Assert.That(Check(d, "EditHistory", contract), Is.Empty);
        }

        [Test]
        public void 必須役割が空なら指摘_任意役割は空でよい()
        {
            var d = Create();
            var contract = Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract");
            contract.UserId = string.Empty;
            contract.DateTime = string.Empty;
            Assert.That(Check(d, "EditHistory", contract), Is.Empty);

            contract.Snapshot = string.Empty;
            var ret = Check(d, "EditHistory", contract);
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("EditHistory", "Contract", nameof(EditHistoryContractFieldDesign.Snapshot));
        }

        [Test]
        public void 役割のフィールドが無ければ指摘()
        {
            var d = Create();
            var contract = Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract");
            contract.DataId = "Nothing";
            var ret = Check(d, "EditHistory", contract);
            Assert.That(ret.Count, Is.EqualTo(1));
            ret[0].AssertFieldLocation("EditHistory", "Contract", nameof(EditHistoryContractFieldDesign.DataId));
        }

        [Test]
        public void 役割のフィールド型が違えば指摘()
        {
            var d = Create();
            var history = Module(d, "EditHistory");
            history.Fields.RemoveAll(e => e.Name == "Snapshot");
            history.Fields.Add(new NumberFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            var contract = Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract");
            var ret = Check(d, "EditHistory", contract);
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo(DesignCheckCode.Create(typeof(EditHistoryContractFieldDesign), 1)));
            ret[0].AssertFieldLocation("EditHistory", "Contract", nameof(EditHistoryContractFieldDesign.Snapshot));

            //ChangeType は Select でもよい
            history.Fields.RemoveAll(e => e.Name == "ChangeType");
            history.Fields.Add(new SelectFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.RemoveAll(e => e.Name == "Snapshot");
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            Assert.That(Check(d, "EditHistory", contract), Is.Empty);
        }

        [Test]
        public void 退避との併用は指摘()
        {
            var d = Create();
            Module(d, "Order").Fields.Add(new DeleteArchiveFieldDesign { Name = "DeleteArchive" });
            var ret = Check(d, "Order", Field<EditHistoryFieldDesign>(d, "Order", "History"));
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo(DesignCheckCode.Create(typeof(EditHistoryFieldDesign), 3)));
            ret[0].AssertFieldLocation("Order", "History", nameof(FieldDesignBase.Name));
        }

        [Test]
        public void 復活ボタンは契約フィールドのあるモジュールにだけ置ける()
        {
            var d = Create();
            var button = new EditHistoryRestoreButtonFieldDesign { Name = "Restore" };
            Module(d, "EditHistory").Fields.Add(button);
            Assert.That(Check(d, "EditHistory", button), Is.Empty);

            Module(d, "Order").Fields.Add(button);
            var ret = Check(d, "Order", button);
            Assert.That(ret.Count, Is.EqualTo(1));
            Assert.That(ret[0].Message, Does.Contain(nameof(EditHistoryContractFieldDesign)));
        }

        [Test]
        public void 履歴モジュールのリネームに追従する()
        {
            var d = Create();
            var field = Field<EditHistoryFieldDesign>(d, "Order", "History");
            var context = new RenameContext(d)
            {
                Type = RenameType.Module, Source = "EditHistory", Destination = "ChangeLog", OwnerModule = "Order",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(field.HistoryModuleName, Is.EqualTo("ChangeLog"));
        }

        [Test]
        public void 契約の役割は履歴モジュールのフィールドリネームに追従する()
        {
            var d = Create();
            var contract = Field<EditHistoryContractFieldDesign>(d, "EditHistory", "Contract");
            var context = new RenameContext(d)
            {
                Type = RenameType.Field, ModuleName = "EditHistory", Source = "Snapshot", Destination = "Json", OwnerModule = "EditHistory",
            };
            var result = contract.ChangeName(context);
            Assert.That(result.RenameNeeded);
            result.RenameAction();
            Assert.That(contract.Snapshot, Is.EqualTo("Json"));
            Assert.That(contract.DataId, Is.EqualTo("DataId"));
        }
    }
}
