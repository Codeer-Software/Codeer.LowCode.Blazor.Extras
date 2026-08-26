using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    public class MailHistoryWriterTest
    {
        static DesignData CreateDesignData(params FieldDesignBase[] fields)
        {
            var history = new ModuleDesign { Name = "MailHistory" };
            foreach (var e in fields) history.Fields.Add(e);
            var designData = new DesignData();
            designData.AddModule(history);
            return designData;
        }

        static MailSendResult CreateResult()
            => new()
            {
                TotalCount = 10,
                SuccessCount = 8,
                Failures =
                {
                    new MailSendFailure { To = "a@example.com", Error = "bounce" },
                    new MailSendFailure { To = "b@example.com", Error = "bad address" },
                }
            };

        [Test]
        public async Task 契約でリネームした履歴モジュールに書ける_空役割は記録しない()
        {
            //役割→フィールド名のマッピングは履歴モジュール上の契約で宣言する (承認と同じ流儀)。
            //空にした役割 (SourceModule/SourceId 等) は記録しない
            var designData = CreateDesignData(
                new DateTimeFieldDesign { Name = "送信日時" },
                new TextFieldDesign { Name = "件名" },
                new NumberFieldDesign { Name = "成功数" },
                new Codeer.LowCode.Blazor.Extras.Designs.MailHistoryContractFieldDesign
                {
                    Name = "Contract",
                    SentAt = "送信日時",
                    Subject = "件名",
                    SuccessCount = "成功数",
                    MailInfraName = "",
                    TotalCount = "",
                    FailureDetails = "",
                    SourceModule = "",
                    SourceId = "",
                });

            ModuleData? written = null;
            var writer = new MailHistoryWriter("MailHistory", designData, d => { written = d; return Task.FromResult("1"); });

            await writer.WriteAsync("Campaign", "8月のご案内", CreateResult(),
                new MailHistorySource { SourceModule = "Campaign", SourceId = "42" });

            Assert.That(written, Is.Not.Null);
            Assert.That(((TextFieldData)written!.Fields["件名"]).Value, Is.EqualTo("8月のご案内"));
            Assert.That(((NumberFieldData)written.Fields["成功数"]).Value, Is.EqualTo(8));
            Assert.That(((DateTimeFieldData)written.Fields["送信日時"]).Value, Is.Not.Null);
            Assert.That(written.Fields.Count, Is.EqualTo(3)); //空役割は書かれない
        }

        [Test]
        public async Task 予約名フィールドに書かれ失敗明細はJSONになる()
        {
            var designData = CreateDesignData(
                new DateTimeFieldDesign { Name = "SentAt" },
                new TextFieldDesign { Name = "MailInfraName" },
                new TextFieldDesign { Name = "Subject" },
                new NumberFieldDesign { Name = "TotalCount" },
                new NumberFieldDesign { Name = "SuccessCount" },
                new TextFieldDesign { Name = "FailureDetails" },
                new TextFieldDesign { Name = "SourceModule" },
                new TextFieldDesign { Name = "SourceId" });

            ModuleData? written = null;
            var writer = new MailHistoryWriter("MailHistory", designData, d => { written = d; return Task.FromResult("1"); });

            await writer.WriteAsync("Campaign", "8月のご案内", CreateResult(),
                new MailHistorySource { SourceModule = "Campaign", SourceId = "42" });

            Assert.That(written, Is.Not.Null);
            Assert.That(((TextFieldData)written!.Fields["MailInfraName"]).Value, Is.EqualTo("Campaign"));
            Assert.That(((TextFieldData)written.Fields["Subject"]).Value, Is.EqualTo("8月のご案内"));
            Assert.That(((NumberFieldData)written.Fields["TotalCount"]).Value, Is.EqualTo(10));
            Assert.That(((NumberFieldData)written.Fields["SuccessCount"]).Value, Is.EqualTo(8));
            Assert.That(((DateTimeFieldData)written.Fields["SentAt"]).Value, Is.Not.Null);
            Assert.That(((TextFieldData)written.Fields["SourceModule"]).Value, Is.EqualTo("Campaign"));
            Assert.That(((TextFieldData)written.Fields["SourceId"]).Value, Is.EqualTo("42"));
            var json = ((TextFieldData)written.Fields["FailureDetails"]).Value!;
            Assert.That(json, Does.Contain("a@example.com").And.Contain("bounce"));
        }

        [Test]
        public async Task 予約名フィールドの部分配置はあるものだけ書かれる()
        {
            var designData = CreateDesignData(
                new TextFieldDesign { Name = "Subject" },
                new NumberFieldDesign { Name = "SuccessCount" });

            ModuleData? written = null;
            var writer = new MailHistoryWriter("MailHistory", designData, d => { written = d; return Task.FromResult("1"); });
            await writer.WriteAsync("Notify", "s", CreateResult(), null);

            Assert.That(written!.Fields.Keys, Is.EquivalentTo(new[] { "Subject", "SuccessCount" }));
        }

        [Test]
        public async Task FailureDetailsはJsonFieldでもよい()
        {
            var designData = CreateDesignData(new JsonFieldDesign { Name = "FailureDetails" });

            ModuleData? written = null;
            var writer = new MailHistoryWriter("MailHistory", designData, d => { written = d; return Task.FromResult("1"); });
            await writer.WriteAsync("Notify", "s", CreateResult(), null);

            Assert.That(((JsonFieldData)written!.Fields["FailureDetails"]).Value, Does.Contain("bad address"));
        }

        [Test]
        public async Task モジュールが無い場合はログだけで例外にしない()
        {
            var errors = new List<string>();
            var writer = new MailHistoryWriter("Nothing", CreateDesignData(), _ => Task.FromResult("1"), errors.Add);
            await writer.WriteAsync("Notify", "s", CreateResult(), null);
            Assert.That(errors.Single(), Does.Contain("Nothing"));
        }

        [Test]
        public async Task 型違いの予約名フィールドはスキップしてログする()
        {
            var designData = CreateDesignData(
                new TextFieldDesign { Name = "Subject" },
                new TextFieldDesign { Name = "TotalCount" }); //Number想定のところにText

            var errors = new List<string>();
            ModuleData? written = null;
            var writer = new MailHistoryWriter("MailHistory", designData, d => { written = d; return Task.FromResult("1"); }, errors.Add);
            await writer.WriteAsync("Notify", "s", CreateResult(), null);

            Assert.That(written!.Fields.Keys, Is.EquivalentTo(new[] { "Subject" }));
            Assert.That(errors.Single(), Does.Contain("TotalCount"));
        }

        [Test]
        public async Task 書き込み失敗はログだけで例外にしない()
        {
            var designData = CreateDesignData(new TextFieldDesign { Name = "Subject" });
            var errors = new List<string>();
            var writer = new MailHistoryWriter("MailHistory", designData, _ => throw new Exception("db down"), errors.Add);
            await writer.WriteAsync("Notify", "s", CreateResult(), null);
            Assert.That(errors.Single(), Does.Contain("db down"));
        }
    }
}
