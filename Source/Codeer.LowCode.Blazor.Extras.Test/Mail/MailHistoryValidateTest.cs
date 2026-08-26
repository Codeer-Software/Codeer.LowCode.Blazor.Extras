using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// 履歴を取る設定 (Mail.HistoryModuleName) なのに契約を満たしていなければ送信前に例外 = 送らない。
    /// ただし**必須以外の役割は空にできる** (= その項目は記録しない)。
    /// </summary>
    public class MailHistoryValidateTest
    {
        static DesignData CreateDesignData(ModuleDesign? history)
        {
            var designData = new DesignData();
            if (history != null) designData.AddModule(history);
            return designData;
        }

        //既定の役割名 (契約フィールドなし) を全部持つ履歴モジュール
        static ModuleDesign CreateFullHistory()
        {
            var m = new ModuleDesign { Name = "MailHistory", DataSourceName = "Main", DbTable = "mail_histories" };
            m.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" });
            m.Fields.Add(new TextFieldDesign { Name = "MailInfraName", DbColumn = "infra" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DbColumn = "subject" });
            m.Fields.Add(new NumberFieldDesign { Name = "TotalCount", DbColumn = "total" });
            m.Fields.Add(new NumberFieldDesign { Name = "SuccessCount", DbColumn = "success" });
            m.Fields.Add(new TextFieldDesign { Name = "FailureDetails", DbColumn = "failures" });
            m.Fields.Add(new TextFieldDesign { Name = "SourceModule", DbColumn = "source_module" });
            m.Fields.Add(new TextFieldDesign { Name = "SourceId", DbColumn = "source_id" });
            return m;
        }

        static MailHistoryWriter CreateWriter(DesignData designData)
            => new("MailHistory", designData, _ => Task.CompletedTask, _ => { });

        [Test]
        public void 既定名が全部揃っていればOK()
            => Assert.DoesNotThrow(() => CreateWriter(CreateDesignData(CreateFullHistory())).Validate());

        [Test]
        public void モジュールが無ければ例外()
            => Assert.Throws<InvalidOperationException>(() => CreateWriter(CreateDesignData(null)).Validate());

        [Test]
        public void 契約なしで必須の送信日時が無ければ例外()
        {
            var history = CreateFullHistory();
            history.Fields.RemoveAll(e => e.Name == "SentAt");
            var ex = Assert.Throws<InvalidOperationException>(() => CreateWriter(CreateDesignData(history)).Validate());
            Assert.That(ex!.Message, Does.Contain("SentAt"));
        }

        [Test]
        public void 契約なしなら必須以外のフィールドが無くても動く()
        {
            //送信日時だけある履歴モジュール = 他の項目は記録しないだけ
            var history = new ModuleDesign { Name = "MailHistory", DataSourceName = "Main", DbTable = "mail_histories" };
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" });
            Assert.DoesNotThrow(() => CreateWriter(CreateDesignData(history)).Validate());
        }

        [Test]
        public void 契約で必須以外を空にすれば動く()
        {
            var history = new ModuleDesign { Name = "MailHistory", DataSourceName = "Main", DbTable = "mail_histories" };
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" });
            history.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            history.Fields.Add(new MailHistoryContractFieldDesign
            {
                Name = "Contract",
                SentAt = "SentAt",
                Subject = "Title",
                MailInfraName = string.Empty,
                TotalCount = string.Empty,
                SuccessCount = string.Empty,
                FailureDetails = string.Empty,
                SourceModule = string.Empty,
                SourceId = string.Empty,
            });
            Assert.DoesNotThrow(() => CreateWriter(CreateDesignData(history)).Validate());
        }

        [Test]
        public void 契約が名指ししたフィールドが無ければ例外()
        {
            var history = new ModuleDesign { Name = "MailHistory", DataSourceName = "Main", DbTable = "mail_histories" };
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "sent_at" });
            history.Fields.Add(new MailHistoryContractFieldDesign
            {
                Name = "Contract",
                SentAt = "SentAt",
                Subject = "NoSuchField",
                MailInfraName = string.Empty,
                TotalCount = string.Empty,
                SuccessCount = string.Empty,
                FailureDetails = string.Empty,
                SourceModule = string.Empty,
                SourceId = string.Empty,
            });
            var ex = Assert.Throws<InvalidOperationException>(() => CreateWriter(CreateDesignData(history)).Validate());
            Assert.That(ex!.Message, Does.Contain("NoSuchField"));
        }

        [Test]
        public void 契約で必須の役割を空にしたら例外()
        {
            var history = CreateFullHistory();
            history.Fields.Add(new MailHistoryContractFieldDesign { Name = "Contract", SentAt = string.Empty });
            var ex = Assert.Throws<InvalidOperationException>(() => CreateWriter(CreateDesignData(history)).Validate());
            Assert.That(ex!.Message, Does.Contain("SentAt"));
        }

        //デザインチェック側: 必須役割が空ならエラー / 必須以外は空でもエラーにしない
        [Test]
        public void デザインチェック_必須役割が空ならエラー()
        {
            var history = CreateFullHistory();
            var contract = new MailHistoryContractFieldDesign { Name = "Contract", SentAt = string.Empty };
            history.Fields.Add(contract);
            var designData = CreateDesignData(history);

            var result = contract.CheckDesign(new DesignLogic.Check.DesignCheckContext("MailHistory", designData,
                new Dictionary<string, List<DataIO.Db.Definition.DbTableDefinition>>()));
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Does.Contain(nameof(MailHistoryContractFieldDesign.SentAt)));
        }

        [Test]
        public void デザインチェック_必須以外は空でもエラーにしない()
        {
            var history = CreateFullHistory();
            var contract = new MailHistoryContractFieldDesign
            {
                Name = "Contract",
                MailInfraName = string.Empty,
                TotalCount = string.Empty,
                SuccessCount = string.Empty,
                FailureDetails = string.Empty,
                SourceModule = string.Empty,
                SourceId = string.Empty,
            };
            history.Fields.Add(contract);
            var designData = CreateDesignData(history);

            var result = contract.CheckDesign(new DesignLogic.Check.DesignCheckContext("MailHistory", designData,
                new Dictionary<string, List<DataIO.Db.Definition.DbTableDefinition>>()));
            Assert.That(result, Is.Empty);
        }
    }
}
