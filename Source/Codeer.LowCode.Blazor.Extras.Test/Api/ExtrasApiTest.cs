using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.Excel;
using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Api
{
    /// <summary>
    /// 共通の API の振り分け (ExtrasApi) と、クライアント側の経路の選び方 (古い結線があればそちら、無ければ共通の口)。
    /// </summary>
    public class ExtrasApiTest
    {
        public class Probe
        {
            public string Text { get; set; } = string.Empty;
        }

        sealed class EchoHandler : IExtrasApiHandler
        {
            public string Feature => "Echo";

            public AuditCategory GetAuditCategory(string action) => action == "Upper" ? AuditCategory.DataWrite : AuditCategory.Other;

            public Task<object?> ExecuteAsync(string action, string payload, ExtrasApiContext context) => action switch
            {
                "Upper" => Task.FromResult<object?>(new Probe { Text = ExtrasApi.ReadPayload<Probe>(payload).Text.ToUpperInvariant() }),
                "Nothing" => Task.FromResult<object?>(null),
                _ => throw ExtrasApi.UnknownAction(Feature, action),
            };
        }

        static ExtrasApiContext Context() => new() { ModuleDataIO = null!, DesignData = new(), DbAccess = null!, UserId = "u1" };

        //PDF 変換はフォントの解決が要る (ホストは起動時に CustomFontResolver を置く)。テストは Windows の Arial で済ませる
        sealed class ArialFontResolver : PdfSharp.Fonts.IFontResolver
        {
            public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
            public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("Arial");
        }

        [OneTimeSetUp]
        public void InstallFontResolver() => PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new ArialFontResolver();

        static ExtrasApi Api() => new() { Handlers = { new EchoHandler() } };

        static ExtrasRequest Request(string action, object? payload = null) => new()
        {
            Feature = "Echo",
            Action = action,
            Payload = payload == null ? string.Empty : JsonConverterEx.SerializeObject(payload),
        };

        [Test]
        public async Task UnknownFeature_ReturnsNull()
        {
            var response = await Api().ExecuteAsync(new ExtrasRequest { Feature = "NoSuchFeature", Action = "X" }, Context());
            Assert.That(response, Is.Null);
        }

        [Test]
        public async Task Payload_RoundTrip()
        {
            var response = await Api().ExecuteAsync(Request("Upper", new Probe { Text = "abc" }), Context());
            Assert.That(JsonConverterEx.DeserializeObject<Probe>(response!.Payload)!.Text, Is.EqualTo("ABC"));
        }

        [Test]
        public async Task NullResult_EmptyPayload()
        {
            var response = await Api().ExecuteAsync(Request("Nothing"), Context());
            Assert.That(response, Is.Not.Null);
            Assert.That(response!.Payload, Is.Empty);
        }

        [Test]
        public void UnknownAction_Throws()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await Api().ExecuteAsync(Request("Bogus"), Context()));
        }

        //監査: 実行の前に分類 (振り分け先の GetAuditCategory) と操作名 "機能.操作" が決まり、試行の行がある分類なら試行の行が書かれる
        [Test]
        public async Task Audit_CategoryAndActionAreDecidedBeforeExecution_AttemptWritten()
        {
            var audit = new AuditContext();
            var attempts = new List<AuditEvent>();
            audit.WriteAttemptAsync = e => { attempts.Add(new AuditEvent { Category = e.Category, Action = e.Action }); return Task.CompletedTask; };
            AuditContext.Current = audit;
            try
            {
                await Api().ExecuteAsync(Request("Upper", new Probe()), Context());
            }
            finally
            {
                AuditContext.Current = null;
            }
            Assert.That(audit.Event.Action, Is.EqualTo("Echo.Upper"));
            Assert.That(audit.Event.Category, Is.EqualTo(AuditCategory.DataWrite));
            Assert.That(attempts, Has.Count.EqualTo(1));
            Assert.That(attempts[0].Category, Is.EqualTo(AuditCategory.DataWrite));
            Assert.That(attempts[0].Action, Is.EqualTo("Echo.Upper"));
        }

        //試行の行が無い分類 (Other) では書かない。前段 (属性) で書かれていれば二重に書かない
        [Test]
        public async Task Audit_NoAttemptForOther_NoDuplicateWhenAlreadyWritten()
        {
            var audit = new AuditContext();
            var attempts = 0;
            audit.WriteAttemptAsync = _ => { attempts++; return Task.CompletedTask; };
            AuditContext.Current = audit;
            try
            {
                await Api().ExecuteAsync(Request("Nothing"), Context());
                Assert.That(attempts, Is.EqualTo(0));

                audit.AttemptWritten = true;
                await Api().ExecuteAsync(Request("Upper", new Probe()), Context());
                Assert.That(attempts, Is.EqualTo(0));
            }
            finally
            {
                AuditContext.Current = null;
            }
        }

        //メール: 専用の結線が無ければ共通の口 (ExtrasTransport) に Feature=Mail / Action=Send で届く
        [Test]
        public async Task MailTransport_UsesExtrasTransportWhenNotConfigured()
        {
            var savedHandler = MailTransport.Handler;
            var savedEndPoint = MailTransport.SendMailEndPoint;
            var savedExtras = ExtrasTransport.Handler;
            ExtrasRequest? captured = null;
            try
            {
                MailTransport.Handler = null;
                MailTransport.SendMailEndPoint = string.Empty;
                ExtrasTransport.Handler = request =>
                {
                    captured = request;
                    return Task.FromResult<ExtrasResponse?>(new ExtrasResponse { Payload = JsonConverterEx.SerializeObject(new MailSendResult { TotalCount = 7 }) });
                };

                var result = await MailTransport.SendAsync(null, new MailSendRequest { MailInfraName = "Smtp" });

                Assert.That(captured!.Feature, Is.EqualTo(MailApiNames.Feature));
                Assert.That(captured.Action, Is.EqualTo(MailApiNames.Send));
                Assert.That(ExtrasApi.ReadPayload<MailSendRequest>(captured.Payload).MailInfraName, Is.EqualTo("Smtp"));
                Assert.That(result.TotalCount, Is.EqualTo(7));
            }
            finally
            {
                MailTransport.Handler = savedHandler;
                MailTransport.SendMailEndPoint = savedEndPoint;
                ExtrasTransport.Handler = savedExtras;
            }
        }

        //メール: 送る先が無い (http も Handler も無い) ときは「未設定」の失敗 (送ったが失敗、とは区別する)
        [Test]
        public async Task MailTransport_NotConfigured_WhenNothingToSendTo()
        {
            var savedHandler = MailTransport.Handler;
            var savedEndPoint = MailTransport.SendMailEndPoint;
            var savedExtras = ExtrasTransport.Handler;
            try
            {
                MailTransport.Handler = null;
                MailTransport.SendMailEndPoint = string.Empty;
                ExtrasTransport.Handler = null;

                var result = await MailTransport.SendAsync(null, new MailSendRequest());

                Assert.That(result.Failures.Single().Error, Does.Contain("not configured"));
            }
            finally
            {
                MailTransport.Handler = savedHandler;
                MailTransport.SendMailEndPoint = savedEndPoint;
                ExtrasTransport.Handler = savedExtras;
            }
        }

        //Excel → PDF: xlsx の中身を Payload (base64) で送り、PDF の中身が返る
        [Test]
        public async Task ExcelApi_ConvertsWorkbookToPdf()
        {
            using var book = new ClosedXML.Excel.XLWorkbook();
            book.AddWorksheet("Sheet1").Cell(1, 1).Value = "hello";
            using var xlsx = new MemoryStream();
            book.SaveAs(xlsx);

            var api = new ExtrasApi { Handlers = { new ExcelApi() } };
            var request = new ExtrasRequest { Feature = ExcelApiNames.Feature, Action = ExcelApiNames.ConvertPdf, Payload = JsonConverterEx.SerializeObject(new ExcelConvertPdfRequest { Content = xlsx.ToArray() }) };
            var response = await api.ExecuteAsync(request, Context());

            var pdf = JsonConverterEx.DeserializeObject<ExcelConvertPdfResponse>(response!.Payload)!.Content;
            Assert.That(pdf.Length, Is.GreaterThan(4));
            Assert.That(System.Text.Encoding.ASCII.GetString(pdf, 0, 4), Is.EqualTo("%PDF"));
        }

        //承認: 専用の結線 (EndPointBase) が無ければ共通の口に Feature=Approval / Action=Execute で届く
        [Test]
        public async Task ApprovalTransport_UsesExtrasTransportWhenNotConfigured()
        {
            var savedEndPoint = ApprovalTransport.EndPointBase;
            var savedExtras = ExtrasTransport.Handler;
            ExtrasRequest? captured = null;
            try
            {
                ApprovalTransport.EndPointBase = string.Empty;
                ExtrasTransport.Handler = request =>
                {
                    captured = request;
                    return Task.FromResult<ExtrasResponse?>(new ExtrasResponse { Payload = JsonConverterEx.SerializeObject(new ApprovalActionResult { IsSuccess = true, TargetId = "t1" }) });
                };

                var result = await ApprovalTransport.ExecuteAsync(null, new ApprovalCommand { TargetModuleName = "Order", Action = ApprovalAction.Approve });

                Assert.That(captured!.Feature, Is.EqualTo(ApprovalApiNames.Feature));
                Assert.That(captured.Action, Is.EqualTo(ApprovalApiNames.Execute));
                Assert.That(ExtrasApi.ReadPayload<ApprovalCommand>(captured.Payload).TargetModuleName, Is.EqualTo("Order"));
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.TargetId, Is.EqualTo("t1"));
            }
            finally
            {
                ApprovalTransport.EndPointBase = savedEndPoint;
                ExtrasTransport.Handler = savedExtras;
            }
        }

        //承認: 専用のルート (EndPointBase) が設定されていれば共通の口は使わない (今までの使い方)
        [Test]
        public async Task ApprovalTransport_OwnRouteWinsOverExtrasTransport()
        {
            var savedEndPoint = ApprovalTransport.EndPointBase;
            var savedExtras = ExtrasTransport.Handler;
            var extrasCalled = false;
            try
            {
                ApprovalTransport.EndPointBase = "/api/approval";
                ExtrasTransport.Handler = _ => { extrasCalled = true; return Task.FromResult<ExtrasResponse?>(null); };

                //http が無いので専用ルートには送れず「未設定」の失敗になる (共通の口には行かない)
                var result = await ApprovalTransport.ExecuteAsync(null, new ApprovalCommand());

                Assert.That(extrasCalled, Is.False);
                Assert.That(result.IsSuccess, Is.False);
            }
            finally
            {
                ApprovalTransport.EndPointBase = savedEndPoint;
                ExtrasTransport.Handler = savedExtras;
            }
        }

        //メール: 専用のルート (SendMailEndPoint) が設定されていれば共通の口は使わない (今までの使い方)
        [Test]
        public async Task MailTransport_OwnRouteWinsOverExtrasTransport()
        {
            var savedHandler = MailTransport.Handler;
            var savedEndPoint = MailTransport.SendMailEndPoint;
            var savedExtras = ExtrasTransport.Handler;
            var extrasCalled = false;
            try
            {
                MailTransport.Handler = null;
                MailTransport.SendMailEndPoint = "/api/mail";
                ExtrasTransport.Handler = _ => { extrasCalled = true; return Task.FromResult<ExtrasResponse?>(null); };

                //http が無いので専用ルートには送れず「未設定」の失敗になる (共通の口には行かない)
                var result = await MailTransport.SendAsync(null, new MailSendRequest());

                Assert.That(extrasCalled, Is.False);
                Assert.That(result.IsSuccess, Is.False);
            }
            finally
            {
                MailTransport.Handler = savedHandler;
                MailTransport.SendMailEndPoint = savedEndPoint;
                ExtrasTransport.Handler = savedExtras;
            }
        }

        //Handler (デスクトップ) の失敗は HTTP の失敗と同じ扱い: onError があればそこへ渡して「届かなかった」、無ければ呼び出し側へ伝わる
        [Test]
        public async Task ExtrasTransport_HandlerFailure_GoesToOnErrorOrPropagates()
        {
            var savedExtras = ExtrasTransport.Handler;
            try
            {
                ExtrasTransport.Handler = _ => throw new InvalidOperationException("boom");

                string? reported = null;
                var (response, delivered) = await ExtrasTransport.CallAsync<Probe>(null, "Echo", "Upper", new Probe(), onError: (_, message) => reported = message);
                Assert.That(response, Is.Null);
                Assert.That(delivered, Is.False);
                Assert.That(reported, Is.EqualTo("boom"));

                Assert.ThrowsAsync<InvalidOperationException>(() => ExtrasTransport.CallAsync<Probe>(null, "Echo", "Upper", new Probe()));
            }
            finally
            {
                ExtrasTransport.Handler = savedExtras;
            }
        }
    }
}
