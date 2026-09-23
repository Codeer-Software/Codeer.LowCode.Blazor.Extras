using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// SemanticSearchText (行 → 文章) と SemanticSearchField の Submit データ:
    /// 表示名・候補値・リンク・日付・真偽の書式、既定の対象フィールド、変更があるときだけ送ること。
    /// </summary>
    public class SemanticSearchTextTest
    {
        static DesignData CreateDesign(params string[] sourceFields)
        {
            var design = new DesignData();
            design.Enums.Add(new EnumDesign
            {
                Name = "InquiryStatus",
                Members = { new EnumMemberDesign { Name = "Open", Value = "1", DisplayText = "受付" }, new EnumMemberDesign { Name = "Closed", Value = "9", DisplayText = "完了" } },
            });
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = "Main", DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body", IsMultiline = true });
            m.Fields.Add(new SelectFieldDesign { Name = "Status", DisplayName = "状態", DbColumn = "status", EnumName = "InquiryStatus" });
            var kind = new SelectFieldDesign { Name = "Kind", DbColumn = "kind" };
            kind.Candidates.AddRange(["クレーム,C", "相談,Q"]);
            m.Fields.Add(kind);
            var link = new LinkFieldDesign { Name = "Customer", DisplayName = "得意先", DbColumn = "customer_id", ValueVariable = "Id", DisplayTextVariable = "Name" };
            link.SearchCondition.ModuleName = "Customer";
            m.Fields.Add(link);
            m.Fields.Add(new DateFieldDesign { Name = "ReceivedOn", DisplayName = "受付日", DbColumn = "received_on" });
            m.Fields.Add(new BooleanFieldDesign { Name = "Urgent", DisplayName = "至急", DbColumn = "urgent" });
            m.Fields.Add(new NumberFieldDesign { Name = "Amount", DisplayName = "金額", DbColumn = "amount" });
            m.Fields.Add(new PasswordFieldDesign { Name = "Secret" });
            m.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "is_deleted" });
            var search = new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" };
            search.SourceFields.AddRange(sourceFields);
            m.Fields.Add(search);
            m.ListLayouts[""] = new ListLayoutDesign();
            design.AddModule(m);

            var customer = new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            design.AddModule(customer);
            return design;
        }

        static ModuleData Row()
        {
            var row = new ModuleData { Name = "Inquiry" };
            row.Fields["Id"] = new IdFieldData { Value = "7" };
            row.Fields["Subject"] = new TextFieldData { Value = "納期遅れの相談" };
            row.Fields["Body"] = new TextFieldData { Value = "先週の注文が\nまだ届きません。" };
            row.Fields["Status"] = new SelectFieldData { Value = "9" };
            row.Fields["Kind"] = new SelectFieldData { Value = "C" };
            row.Fields["Customer"] = new LinkFieldData { Value = "3", DisplayText = "青山商事" };
            row.Fields["ReceivedOn"] = new DateFieldData { Value = new DateOnly(2026, 9, 1) };
            row.Fields["Urgent"] = new BooleanFieldData { Value = true };
            row.Fields["Amount"] = new NumberFieldData { Value = 12000.5m };
            row.Fields["Secret"] = new PasswordFieldData { Value = "p@ss" };
            row.Fields["LogicalDelete"] = new BooleanFieldData { Value = false };
            return row;
        }

        [Test]
        public void 既定の対象は入力フィールド全部で表示名と表示値を使う()
        {
            var design = CreateDesign();
            var module = design.Modules.Find("Inquiry")!;
            var text = SemanticSearchText.Build(design, module, Row(), module.Fields.OfType<SemanticSearchFieldDesign>().Single());
            Assert.That(text, Is.EqualTo(
                "件名: 納期遅れの相談\n" +
                "本文: 先週の注文が\nまだ届きません。\n" +
                "状態: 完了\n" +
                "Kind: クレーム\n" +
                "得意先: 青山商事\n" +
                "受付日: 2026-09-01\n" +
                "至急: はい\n" +
                "金額: 12000.5"));
            //Id・パスワード・論理削除・自分自身は入らない
            Assert.That(text, Does.Not.Contain("p@ss").And.Not.Contain("Id").And.Not.Contain("LogicalDelete"));
        }

        [Test]
        public void 対象フィールドを指定するとその順で空の値は出さない()
        {
            var design = CreateDesign("Body", "Subject", "Amount");
            var module = design.Modules.Find("Inquiry")!;
            var row = Row();
            row.Fields["Amount"] = new NumberFieldData { Value = null };
            var text = SemanticSearchText.Build(design, module, row, module.Fields.OfType<SemanticSearchFieldDesign>().Single());
            Assert.That(text, Is.EqualTo("本文: 先週の注文が\nまだ届きません。\n件名: 納期遅れの相談"));
        }

        [Test]
        public void 最大文字数で切る()
        {
            var design = CreateDesign("Body");
            var module = design.Modules.Find("Inquiry")!;
            var field = module.Fields.OfType<SemanticSearchFieldDesign>().Single();
            field.MaxTextLength = 7;
            Assert.That(SemanticSearchText.Build(design, module, Row(), field), Is.EqualTo("本文: 先週の"));
        }

        [Test]
        public void リッチテキストはタグを除く()
        {
            var design = CreateDesign();
            var text = SemanticSearchText.FormatValue(design, new TextFieldDesign { Name = "X" }, new RichTextFieldData { Value = "<p>納期が<b>遅い</b>&amp;高い</p>" });
            Assert.That(text!.Trim(), Is.EqualTo("納期が 遅い &高い"));
        }

        [Test]
        public async Task フィールドは対象が変更されたときだけ文章を送る()
        {
            var design = CreateDesign();
            var services = new TestServices(design);
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, Row());
            var field = (SemanticSearchField)module.GetField("Search")!;

            Assert.That(module.IsNewData, Is.False);
            Assert.That(field.IsModified, Is.False);
            Assert.That(field.GetData(), Is.Null);
            Assert.That(field.GetSubmitData().FieldData, Is.Null, "変更なしでは送らない");
            Assert.That(field.Text, Does.StartWith("件名: 納期遅れの相談"));

            await ((TextField)module.GetField("Body")!).SetValueAsync("届きました。ありがとう");
            var sent = field.GetSubmitData().FieldData as SemanticSearchFieldData;
            Assert.That(sent, Is.Not.Null);
            Assert.That(sent!.Text, Does.Contain("本文: 届きました。ありがとう"));
            Assert.That(sent.Vector, Is.Null, "ベクトルはサーバーが付ける");

            //モジュールの Submit データにも載る
            var submit = module.GetSubmitData();
            Assert.That(submit.Update.Single().Fields["Search"], Is.TypeOf<SemanticSearchFieldData>());
        }

        [Test]
        public async Task 新規行は常に文章を送る()
        {
            var design = CreateDesign();
            var services = new TestServices(design);
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, new ModuleData { Name = "Inquiry" });
            Assert.That(module.IsNewData, Is.True);
            await ((TextField)module.GetField("Subject")!).SetValueAsync("初めての問い合わせ");
            var sent = module.GetSubmitData().Add.Single().Fields["Search"] as SemanticSearchFieldData;
            Assert.That(sent?.Text, Is.EqualTo("件名: 初めての問い合わせ"));
        }

        [Test]
        public async Task 列が未設定なら何も送らない()
        {
            var design = CreateDesign();
            design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single().DbColumnVector = "";
            var services = new TestServices(design);
            var module = await ModuleCreationService.CreateModuleAsync(services.Core, new ModuleData { Name = "Inquiry" });
            await ((TextField)module.GetField("Subject")!).SetValueAsync("x");
            Assert.That(module.GetSubmitData().Add.Single().Fields.ContainsKey("Search"), Is.False);
        }
    }
}
