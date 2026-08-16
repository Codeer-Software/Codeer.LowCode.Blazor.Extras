using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    //単発メール送信フィールド。テンプレート解決と送信リクエストの組み立て (送信は Handler で捕捉)
    public class MailFieldTest
    {
        class CaptureHandler : IMailTransportHandler
        {
            public MailSendRequest? Sent;

            public Task<MailSendResult> SendAsync(MailSendRequest request)
            {
                Sent = request;
                return Task.FromResult(new MailSendResult { TotalCount = 1, SuccessCount = 1 });
            }

            public Task<MailSendResult> SendBulkSearchAsync(MailBulkSearchRequest request)
                => throw new NotSupportedException();
        }

        static DesignData CreateDesignData(Action<MailFieldDesign>? customize = null)
        {
            var d = new DesignData();
            var mod = new ModuleDesign { Name = "Request" };
            mod.Fields.Add(new IdFieldDesign { Name = "Id" });
            mod.Fields.Add(new TextFieldDesign { Name = "Title" });
            mod.Fields.Add(new TextFieldDesign { Name = "Email" });
            var mail = new MailFieldDesign
            {
                Name = "Notify",
                ToVariable = "Email.Value",
                Subject = "申請 {Title.Value}",
                Body = "{Title.Value} を受け付けました",
                SenderName = "notify",
            };
            customize?.Invoke(mail);
            mod.Fields.Add(mail);
            d.AddModule(mod);
            return d;
        }

        static async Task<(Module Module, MailField Field)> CreateAsync(DesignData designData)
        {
            var services = new TestServices(designData);
            var module = await services.CreateModuleAsync("Request");
            return (module, (MailField)module.GetField("Notify")!);
        }

        [Test]
        public async Task 送信_テンプレートを自レコードで解決してリクエストを組み立てる()
        {
            var (module, field) = await CreateAsync(CreateDesignData());
            await ((TextField)module.GetField("Title")!).SetValueAsync("経費精算");
            await ((TextField)module.GetField("Email")!).SetValueAsync("a@example.com, b@example.com");

            var handler = new CaptureHandler();
            MailTransport.Handler = handler;
            try
            {
                var result = await field.SendAsync();
                Assert.That(result.IsSuccess, Is.True);
            }
            finally
            {
                MailTransport.Handler = null;
            }

            var sent = handler.Sent!;
            Assert.That(sent.SenderName, Is.EqualTo("notify"));
            Assert.That(sent.SourceModule, Is.EqualTo("Request"));
            Assert.That(sent.Message.To, Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
            Assert.That(sent.Message.Subject, Is.EqualTo("申請 経費精算"));
            Assert.That(sent.Message.Body, Is.EqualTo("経費精算 を受け付けました"));
        }

        [Test]
        public async Task 送信_固定宛先とCc()
        {
            var (module, field) = await CreateAsync(CreateDesignData(m =>
            {
                m.ToVariable = "";
                m.To = "fixed@example.com";
                m.Cc = "cc1@example.com; cc2@example.com";
            }));
            await ((TextField)module.GetField("Title")!).SetValueAsync("T");

            var handler = new CaptureHandler();
            MailTransport.Handler = handler;
            try
            {
                await field.SendAsync();
            }
            finally
            {
                MailTransport.Handler = null;
            }

            Assert.That(handler.Sent!.Message.To, Is.EqualTo(new[] { "fixed@example.com" }));
            Assert.That(handler.Sent.Message.Cc, Is.EqualTo(new[] { "cc1@example.com", "cc2@example.com" }));
        }

        [Test]
        public async Task 送信_宛先が解決できなければ失敗を返し送信しない()
        {
            var (_, field) = await CreateAsync(CreateDesignData()); //Email 未入力

            var handler = new CaptureHandler();
            MailTransport.Handler = handler;
            try
            {
                var result = await field.SendAsync();
                Assert.That(result.IsSuccess, Is.False);
            }
            finally
            {
                MailTransport.Handler = null;
            }
            Assert.That(handler.Sent, Is.Null);
        }

        [Test]
        public void チェック_宛先未設定と件名本文空は指摘される()
        {
            var d = CreateDesignData(m =>
            {
                m.ToVariable = "";
                m.To = "";
                m.Subject = "";
                m.Body = "";
            });
            var field = (MailFieldDesign)d.Modules.Find("Request")!.Fields.First(e => e.Name == "Notify");

            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(2)); //宛先必須 + 件名/本文必須
        }

        [Test]
        public void チェック_変数の存在検証とリネーム追従()
        {
            var d = CreateDesignData(m => m.ToVariable = "Missing.Value");
            var field = (MailFieldDesign)d.Modules.Find("Request")!.Fields.First(e => e.Name == "Notify");
            var ret = field.CheckDesign(new DesignCheckContext("Request", d, Utilities.CreateDataSource()));
            Assert.That(ret.Count, Is.EqualTo(1));

            //リネーム追従 (宛先変数)
            field.ToVariable = "Email.Value";
            var context = new Codeer.LowCode.Blazor.DesignLogic.Refactor.RenameContext(d)
            {
                Type = Codeer.LowCode.Blazor.DesignLogic.Refactor.RenameType.Field,
                ModuleName = "Request",
                OwnerModule = "Request",
                Source = "Email",
                Destination = "MailAddress",
            };
            var result = field.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.ToVariable, Is.EqualTo("MailAddress.Value"));
        }
    }
}
