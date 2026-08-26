using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// 送信明細 (履歴契約の Details → 明細モジュール。1 宛先 1 行に解決後の件名・本文と成否)。
    /// 明細は任意: Details が空なら書かれない。
    /// </summary>
    public class MailHistoryDetailTest
    {
        static DesignData CreateDesignData(bool withDetails, bool detailContract = true)
        {
            var d = new DesignData();
            var history = new ModuleDesign { Name = "MailHistory" };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt" });
            history.Fields.Add(new TextFieldDesign { Name = "Subject" });
            if (withDetails)
            {
                history.Fields.Add(new ListFieldDesign
                {
                    Name = "Details",
                    SearchCondition = new SearchCondition("MailHistoryDetail")
                    {
                        Condition = new FieldVariableMatchCondition
                        { SearchTargetVariable = "History.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                    },
                });
            }
            //使わない役割は空 (契約があるので既定名の不在はエラーになる)
            history.Fields.Add(new MailHistoryContractFieldDesign
            {
                Name = "Contract", Details = withDetails ? "Details" : string.Empty,
                MailInfraName = "", TotalCount = "", SuccessCount = "", FailureDetails = "", SourceModule = "", SourceId = "",
            });
            d.AddModule(history);

            var detail = new ModuleDesign { Name = "MailHistoryDetail" };
            detail.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            detail.Fields.Add(new LinkFieldDesign { Name = "History", SearchCondition = new SearchCondition("MailHistory") });
            detail.Fields.Add(new TextFieldDesign { Name = "To" });
            detail.Fields.Add(new TextFieldDesign { Name = "Subject" });
            detail.Fields.Add(new TextFieldDesign { Name = "Body" });
            detail.Fields.Add(new BooleanFieldDesign { Name = "IsSuccess" });
            detail.Fields.Add(new TextFieldDesign { Name = "Error" });
            if (detailContract) detail.Fields.Add(new MailHistoryDetailContractFieldDesign { Name = "Contract" });
            d.AddModule(detail);
            return d;
        }

        //内部 add 経路の代役: 書かれた行を集め、Id を採番して返す
        static (MailHistoryWriter Writer, List<ModuleData> Written) CreateWriter(DesignData d, List<string>? errors = null)
        {
            var written = new List<ModuleData>();
            var writer = new MailHistoryWriter("MailHistory", d, data =>
            {
                written.Add(data);
                return Task.FromResult(written.Count.ToString());
            }, errors == null ? null : errors.Add);
            return (writer, written);
        }

        static string S(ModuleData data, string field) => ((ValueFieldDataBase<string>)data.Fields[field]).Value ?? string.Empty;

        [Test]
        public async Task 明細は履歴行のIdを参照して1宛先1行で書かれる()
        {
            var (writer, written) = CreateWriter(CreateDesignData(withDetails: true));
            writer.Validate();

            var result = new MailSendResult
            {
                TotalCount = 2, SuccessCount = 1,
                Failures = { new MailSendFailure { To = "b@example.com", Error = "bounce" } },
            };
            await writer.WriteAsync("Gmail", "件名テンプレート", result, null, new[]
            {
                new MailHistoryDetail { To = "a@example.com", Subject = "A さんへ", Body = "本文 A", IsSuccess = true },
                new MailHistoryDetail { To = "b@example.com", Subject = "B さんへ", Body = "本文 B", IsSuccess = false, Error = "bounce" },
            });

            Assert.That(written.Count, Is.EqualTo(3)); //履歴 1 + 明細 2
            Assert.That(written[0].Name, Is.EqualTo("MailHistory"));
            Assert.That(written[1].Name, Is.EqualTo("MailHistoryDetail"));
            Assert.That(S(written[1], "History"), Is.EqualTo("1")); //履歴行の Id
            Assert.That(S(written[1], "To"), Is.EqualTo("a@example.com"));
            Assert.That(S(written[1], "Subject"), Is.EqualTo("A さんへ"));
            Assert.That(S(written[1], "Body"), Is.EqualTo("本文 A"));
            Assert.That(((BooleanFieldData)written[1].Fields["IsSuccess"]).Value, Is.True);
            Assert.That(S(written[2], "To"), Is.EqualTo("b@example.com"));
            Assert.That(((BooleanFieldData)written[2].Fields["IsSuccess"]).Value, Is.False);
            Assert.That(S(written[2], "Error"), Is.EqualTo("bounce"));
        }

        [Test]
        public async Task 履歴契約のDetailsが空なら明細は書かれない()
        {
            var (writer, written) = CreateWriter(CreateDesignData(withDetails: false));
            writer.Validate();
            await writer.WriteAsync("Gmail", "s", MailSendResult.Success(1), null,
                new[] { new MailHistoryDetail { To = "a@example.com", Subject = "s", Body = "b", IsSuccess = true } });
            Assert.That(written.Select(e => e.Name), Is.EqualTo(new[] { "MailHistory" }));
        }

        [Test]
        public void Detailsの先のモジュールが明細契約を満たさなければ送信前に例外()
        {
            //必須役割 To のフィールドが無い
            var d = CreateDesignData(withDetails: true);
            var detail = d.Modules.Find("MailHistoryDetail")!;
            detail.Fields.Remove(detail.Fields.First(e => e.Name == "To"));
            var (writer, _) = CreateWriter(d);
            var ex = Assert.Throws<InvalidOperationException>(() => writer.Validate());
            Assert.That(ex!.Message, Does.Contain("MailHistoryDetail").And.Contain("To"));

            //Details が一覧フィールドでない
            var d2 = CreateDesignData(withDetails: true);
            var history = d2.Modules.Find("MailHistory")!;
            history.Fields.Remove(history.Fields.First(e => e.Name == "Details"));
            history.Fields.Add(new TextFieldDesign { Name = "Details" });
            var (writer2, _) = CreateWriter(d2);
            Assert.That(Assert.Throws<InvalidOperationException>(() => writer2.Validate())!.Message, Does.Contain("not a list field"));
        }

        [Test]
        public async Task 明細契約の任意役割を空にするとその項目は書かれない()
        {
            var d = CreateDesignData(withDetails: true);
            var contract = d.Modules.Find("MailHistoryDetail")!.Fields.OfType<MailHistoryDetailContractFieldDesign>().Single();
            contract.Body = string.Empty;
            contract.Error = string.Empty;
            var (writer, written) = CreateWriter(d);
            writer.Validate();
            await writer.WriteAsync("Gmail", "s", MailSendResult.Success(1), null,
                new[] { new MailHistoryDetail { To = "a@example.com", Subject = "s", Body = "b", IsSuccess = true } });
            Assert.That(written[1].Fields.Keys, Is.EquivalentTo(new[] { "History", "To", "Subject", "IsSuccess" }));
        }

        //---- ディスパッチャ経由: 一斉送信は宛先ごとにテンプレートを解決した文面が明細になる ----

        class FakeSender : IMailSender
        {
            public int MaxBulkCount => 10000;
            public Task<MailSendResult> SendAsync(MailMessage message) => Task.FromResult(MailSendResult.Success(1));
            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
                => Task.FromResult(new MailSendResult
                {
                    TotalCount = recipients.Count,
                    SuccessCount = recipients.Count - 1,
                    Failures = { new MailSendFailure { To = recipients[^1].To, Error = "rejected" } },
                });
        }

        [Test]
        public async Task 一斉送信の明細は宛先ごとに解決した件名本文と成否になる()
        {
            var (writer, written) = CreateWriter(CreateDesignData(withDetails: true));
            var dispatcher = new MailDispatcher(new MailConfig { DefaultInfraName = "Main" }, _ => new FakeSender(), writer);

            var template = new MailBulkTemplate { Subject = "{Name} 様へ", Body = "{Name} 様\n{Rank} 会員のご案内" };
            var recipients = new List<MailBulkRecipient>
            {
                new() { To = "a@example.com", Variables = { ["Name"] = "佐藤", ["Rank"] = "ゴールド" } },
                new() { To = "b@example.com", Variables = { ["Name"] = "鈴木", ["Rank"] = "シルバー" } },
            };
            var result = await dispatcher.SendBulkAsync(null, template, recipients);
            Assert.That(result.SuccessCount, Is.EqualTo(1));

            Assert.That(written.Count, Is.EqualTo(3));
            Assert.That(S(written[0], "Subject"), Is.EqualTo("{Name} 様へ")); //履歴行はテンプレート
            Assert.That(S(written[1], "Subject"), Is.EqualTo("佐藤 様へ"));  //明細は解決後
            Assert.That(S(written[1], "Body"), Is.EqualTo("佐藤 様\nゴールド 会員のご案内"));
            Assert.That(((BooleanFieldData)written[1].Fields["IsSuccess"]).Value, Is.True);
            Assert.That(S(written[2], "To"), Is.EqualTo("b@example.com"));
            Assert.That(S(written[2], "Subject"), Is.EqualTo("鈴木 様へ"));
            Assert.That(((BooleanFieldData)written[2].Fields["IsSuccess"]).Value, Is.False);
            Assert.That(S(written[2], "Error"), Is.EqualTo("rejected"));
        }

        [Test]
        public async Task 単発送信の明細は宛先ごとに同じ文面で書かれる()
        {
            var (writer, written) = CreateWriter(CreateDesignData(withDetails: true));
            var dispatcher = new MailDispatcher(new MailConfig { DefaultInfraName = "Main" }, _ => new FakeSender(), writer);

            var message = new MailMessage { To = { "a@example.com", "b@example.com" }, Subject = "件名", Body = "本文" };
            await dispatcher.SendAsync(null, message);

            Assert.That(written.Select(e => e.Name), Is.EqualTo(new[] { "MailHistory", "MailHistoryDetail", "MailHistoryDetail" }));
            Assert.That(S(written[1], "To"), Is.EqualTo("a@example.com"));
            Assert.That(S(written[2], "To"), Is.EqualTo("b@example.com"));
            Assert.That(S(written[2], "Body"), Is.EqualTo("本文"));
            Assert.That(((BooleanFieldData)written[2].Fields["IsSuccess"]).Value, Is.True);
        }
    }
}
