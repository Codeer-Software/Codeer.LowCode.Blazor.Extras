using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Excel;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Jobs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using ExcelObject = Codeer.LowCode.Blazor.Extras.ScriptObjects.Excel;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// Job スクリプトからメール (MailService) と Excel (OverWrite(ModuleData) / ToAttachment / ToPdfAttachment) を使う (MailApi.UseInJobScripts / ExcelApi.UseInJobScripts)。
    /// DB は SQLite のファイル (Job のトランザクションの取り消しを確かめるため)。メールは実際には送らない (記録するだけの IMailSender)。
    /// 登録は static (本体の JobExecutor と MailApi / ExcelApi) なので、テストごとに消して戻す。Excel.ConvertPdf も元に戻す。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobScriptMailExcelTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        DesignData _designData = null!;
        FakeMailSender _sender = null!;
        Func<MemoryStream, MemoryStream>? _convertPdf;
        readonly Dictionary<string, byte[]> _resources = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        class FakeMailSender : IMailSender
        {
            public List<MailMessage> Sent { get; } = new();
            public Func<MailMessage, MailSendResult>? Result { get; set; }
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                Sent.Add(message);
                return Task.FromResult(Result?.Invoke(message) ?? MailSendResult.Success(1));
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients) => throw new NotSupportedException();
        }

        //PDF 変換はフォントの解決が要る (ホストは起動時に CustomFontResolver を置く)。テストは Windows の Arial で済ませる (ExtrasApiTest と同じ)
        sealed class ArialFontResolver : PdfSharp.Fonts.IFontResolver
        {
            public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
            public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("Arial");
        }

        //本体の JobExecutor.ClearScriptTypes (internal・テスト用)。本体は Extras のテストに IVT を出していないのでリフレクションで呼ぶ
        static void ClearScriptTypes()
        {
            typeof(JobExecutor).GetMethod("ClearScriptTypes", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
            MailApi.ResetJobScriptsForTest();
            ExcelApi.ResetJobScriptsForTest();
        }

        [SetUp]
        public async Task SetUp()
        {
            ClearScriptTypes();
            _convertPdf = ExcelObject.ConvertPdf;
            ExcelObject.ConvertPdf = null;
            _resources.Clear();

            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"job_mail_excel_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id INTEGER PRIMARY KEY, Name TEXT, Email TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers (Id, Name, Email) VALUES (1, '管理者', 'admin@example.com')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Tasks (Id INTEGER PRIMARY KEY, Title TEXT, Status TEXT, AssigneeEmail TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Tasks (Id, Title, Status, AssigneeEmail) VALUES (1, '見積', 'Open', 'a@example.com'), (2, '請求', 'Done', 'b@example.com'), (3, '発注', 'Open', 'c@example.com')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE MailHistory (Id INTEGER PRIMARY KEY, SentAt TEXT, MailInfraName TEXT, Subject TEXT, TotalCount INTEGER, SuccessCount INTEGER, FailureDetails TEXT, SourceModule TEXT, SourceId TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Customers (Id, Name) VALUES (10, 'Codeer')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Title TEXT, Amount NUMERIC, OrderedOn TEXT, CustomerId INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Orders (Id, Title, Amount, OrderedOn, CustomerId) VALUES (1, 'Order-1', 1500, '2026-11-16', 10)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER, Item TEXT, Qty NUMERIC)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO OrderLines (Id, OrderId, Item, Qty) VALUES (1, 1, 'Apple', 3), (2, 1, 'Banana', 5)", new());
            _designData = CreateDesignData();
            _sender = new FakeMailSender();
        }

        [TearDown]
        public void TearDown()
        {
            ClearScriptTypes();
            ExcelObject.ConvertPdf = _convertPdf;
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
        }

        static DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "Email" });
            d.AddModule(user);

            var task = new ModuleDesign { Name = "TaskItem", DataSourceName = Ds, DbTable = "Tasks" };
            task.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            task.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            task.Fields.Add(new TextFieldDesign { Name = "Status", DbColumn = "Status" });
            task.Fields.Add(new TextFieldDesign { Name = "AssigneeEmail", DbColumn = "AssigneeEmail" });
            d.AddModule(task);

            //Mail.HistoryModuleName のモジュール (役割は予約名のフィールド)
            var history = new ModuleDesign { Name = "MailHistory", DataSourceName = Ds, DbTable = "MailHistory" };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "SentAt", DbColumn = "SentAt" });
            history.Fields.Add(new TextFieldDesign { Name = "MailInfraName", DbColumn = "MailInfraName" });
            history.Fields.Add(new TextFieldDesign { Name = "Subject", DbColumn = "Subject" });
            history.Fields.Add(new NumberFieldDesign { Name = "TotalCount", DbColumn = "TotalCount" });
            history.Fields.Add(new NumberFieldDesign { Name = "SuccessCount", DbColumn = "SuccessCount" });
            history.Fields.Add(new TextFieldDesign { Name = "FailureDetails", DbColumn = "FailureDetails" });
            history.Fields.Add(new TextFieldDesign { Name = "SourceModule", DbColumn = "SourceModule" });
            history.Fields.Add(new TextFieldDesign { Name = "SourceId", DbColumn = "SourceId" });
            d.AddModule(history);

            var customer = new ModuleDesign { Name = "Customer", DataSourceName = Ds, DbTable = "Customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            d.AddModule(customer);

            var order = new ModuleDesign { Name = "Order", DataSourceName = Ds, DbTable = "Orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            order.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DbColumn = "Amount" });
            order.Fields.Add(new DateFieldDesign { Name = "OrderedOn", DbColumn = "OrderedOn" });
            var link = new LinkFieldDesign { Name = "Customer", DbColumn = "CustomerId", ValueVariable = "Id.Value", DisplayTextVariable = "Name.Value" };
            link.SearchCondition.ModuleName = "Customer";
            order.Fields.Add(link);
            //リンク越しの項目 (ドットを含む名前)
            order.Fields.Add(new TextFieldDesign { Name = "Customer.Name", DbColumn = "Name" });
            order.Fields.Add(new TextFieldDesign { Name = "Memo" });
            order.Fields.Add(new ListFieldDesign
            {
                Name = "Lines",
                SearchCondition = new SearchCondition("OrderLine")
                {
                    Condition = new FieldVariableMatchCondition { SearchTargetVariable = "Order.Value", Comparison = MatchComparison.Equal, Variable = "Id.Value" },
                    SortConditions = { new SortCondition { Variable = "Id.Value" } },
                },
            });
            d.AddModule(order);

            var line = new ModuleDesign { Name = "OrderLine", DataSourceName = Ds, DbTable = "OrderLines" };
            line.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            var orderLink = new LinkFieldDesign { Name = "Order", DbColumn = "OrderId", ValueVariable = "Id.Value", DisplayTextVariable = "Title.Value" };
            orderLink.SearchCondition.ModuleName = "Order";
            line.Fields.Add(orderLink);
            line.Fields.Add(new TextFieldDesign { Name = "Item", DbColumn = "Item" });
            line.Fields.Add(new NumberFieldDesign { Name = "Qty", DbColumn = "Qty" });
            //子一覧として読まれる項目は一覧レイアウトに置いた項目 (画面と同じ)
            line.ListLayouts[""] = new ListLayoutDesign { Elements = [new List<ListElement> { new() { FieldName = "Item" }, new() { FieldName = "Qty" } }] };
            d.AddModule(line);
            return d;
        }

        ModuleDataIO CreateModuleDataIO() => new(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        Task<string?> RunAsync(string script)
        {
            _designData.JobScripts["TestJob"] = script;
            return JobScripts.RunAsync(new JobExecutionContext
            {
                ModuleDataIO = CreateModuleDataIO(),
                DesignData = _designData,
                GetResource = path => _resources.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null,
            }, "TestJob", []);
        }

        void UseMail(Action<MailConfig>? configure = null)
        {
            var config = new MailConfig { DefaultInfraName = "Main" };
            configure?.Invoke(config);
            MailApi.UseInJobScripts(config, name => name == "Main" ? _sender : null);
        }

        async Task<List<IDictionary<string, object>>> HistoryAsync() => await _db.QueryAsync(Ds, "SELECT * FROM MailHistory ORDER BY Id", new());

        // ===== メール =====

        [Test]
        public async Task Sendで宛先と件名と本文と添付が届き戻り値で成否が分かる()
        {
            UseMail();
            var message = await RunAsync("""
                string Run(JobArguments args)
                {
                    var m = new MailMessage();
                    m.To.Add("a@example.com");
                    m.Cc.Add("c@example.com");
                    m.Subject = "件名";
                    m.Body = "本文";
                    var a = new MailAttachment();
                    a.FileName = "a.txt";
                    a.ContentBase64 = "QUJD";
                    m.Attachments.Add(a);
                    var r = MailService.Send(m);
                    return $"{r.IsSuccess} {r.TotalCount} {r.SuccessCount} {r.Failures.Count}";
                }
                """);
            Assert.That(message, Is.EqualTo("True 1 1 0"));
            Assert.That(_sender.Sent, Has.Count.EqualTo(1));
            var sent = _sender.Sent[0];
            Assert.That(sent.To, Is.EqualTo(new[] { "a@example.com" }));
            Assert.That(sent.Cc, Is.EqualTo(new[] { "c@example.com" }));
            Assert.That(sent.Subject, Is.EqualTo("件名"));
            Assert.That(sent.Body, Is.EqualTo("本文"));
            Assert.That(sent.Attachments.Single().FileName, Is.EqualTo("a.txt"));
            Assert.That(sent.Attachments.Single().ContentBase64, Is.EqualTo("QUJD"));
        }

        [Test]
        public async Task 送信の失敗は戻り値のFailuresで分かる_例外にならない()
        {
            UseMail();
            _sender.Result = m => MailSendResult.Failure(m.To[0], "mailbox unavailable");
            var message = await RunAsync("""
                string Run(JobArguments args)
                {
                    var m = new MailMessage();
                    m.To.Add("x@example.com");
                    m.Subject = "s";
                    var r = MailService.Send(m);
                    if (r.IsSuccess) return "success";
                    return r.Failures[0].To + ":" + r.Failures[0].Error;
                }
                """);
            Assert.That(message, Is.EqualTo("x@example.com:mailbox unavailable"));
        }

        [Test]
        public async Task 検索したレコードごとに送れる_CurrentUserの項目を本文に入れる()
        {
            UseMail();
            var message = await RunAsync("""
                string Run(JobArguments args)
                {
                    var search = new ModuleSearcher<TaskItem>();
                    search.AddEquals(e => e.Status.Value, "Open");
                    var rows = search.ExecuteRaw();
                    foreach (var row in rows)
                    {
                        var m = new MailMessage();
                        m.To.Add(row.AssigneeEmail.Value);
                        m.Subject = "期限が近いタスク: " + row.Title.Value;
                        m.Body = row.Title.Value + " の期限が近づいています。(" + CurrentUser.Name.Value + ")";
                        var r = MailService.Send(m);
                        if (!r.IsSuccess) Fail(r.Failures[0].Error);
                    }
                    return $"{rows.Count} 件";
                }
                """);
            Assert.That(message, Is.EqualTo("2 件"));
            //検索の並びは指定していないので順不同で見る
            Assert.That(_sender.Sent.Select(e => e.To.Single()), Is.EquivalentTo(new[] { "a@example.com", "c@example.com" }));
            Assert.That(_sender.Sent.Select(e => e.Subject), Is.EquivalentTo(new[] { "期限が近いタスク: 見積", "期限が近いタスク: 発注" }));
            Assert.That(_sender.Sent.Single(e => e.To.Single() == "a@example.com").Body, Is.EqualTo("見積 の期限が近づいています。(管理者)"));
        }

        const string SendOneScript = """
            void Send()
            {
                var m = new MailMessage();
                m.To.Add("a@example.com");
                m.Subject = "お知らせ";
                MailService.Send(m);
            }
            """;

        [Test]
        public async Task 送信履歴の設定があれば履歴の行がDBに入る()
        {
            UseMail(c => c.HistoryModuleName = "MailHistory");
            await RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                }
                """);
            var rows = await HistoryAsync();
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0]["Subject"], Is.EqualTo("お知らせ"));
            Assert.That(rows[0]["MailInfraName"], Is.EqualTo("Main"));
            Assert.That(Convert.ToInt32(rows[0]["SuccessCount"]), Is.EqualTo(1));
        }

        [Test]
        public async Task Jobが後で失敗すると履歴は消えるがメールは送られている()
        {
            UseMail(c => c.HistoryModuleName = "MailHistory");
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                    Fail("後の処理で失敗");
                }
                """));
            Assert.That(ex!.Message, Does.Contain("後の処理で失敗"));
            //メールは取り消せない
            Assert.That(_sender.Sent, Has.Count.EqualTo(1));
            //履歴は Job のトランザクションで書いたので取り消される
            Assert.That(await HistoryAsync(), Is.Empty);
        }

        [Test]
        public async Task 送った後に確定すれば後で失敗しても履歴は残る()
        {
            UseMail(c => c.HistoryModuleName = "MailHistory");
            Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                    DBService.Commit();
                    Fail("後の処理で失敗");
                }
                """));
            Assert.That(_sender.Sent, Has.Count.EqualTo(1));
            Assert.That(await HistoryAsync(), Has.Count.EqualTo(1));
        }

        //確定 → 確定前の書き込み → 送信。送信の後で Job が失敗すれば、確定前の分だけ消える
        const string AddTaskAndSendScript = """
            void AddTask(string title)
            {
                var data = new TaskItem.Data();
                data.Title = new TextFieldData();
                data.Title.Value = title;
                DBService.Add(data);
            }

            void Run(JobArguments args)
            {
                AddTask("確定済");
                DBService.Commit();
                AddTask("未確定");
                Send();
            }
            """;

        async Task<List<string>> TaskTitlesAsync()
            => (await _db.QueryAsync(Ds, "SELECT Title FROM Tasks ORDER BY Id", new())).Select(e => (string)e["Title"]).ToList();

        [Test]
        public async Task 送信履歴を書けなければJobは失敗し確定前の書き込みは消える_メールは送られている()
        {
            //履歴の INSERT を DB の制約で落とす
            await _db.ExecuteAsync(Ds, "DROP TABLE MailHistory", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE MailHistory (Id INTEGER PRIMARY KEY, SentAt TEXT, MailInfraName TEXT, Subject TEXT CHECK (Subject <> 'お知らせ'), TotalCount INTEGER, SuccessCount INTEGER, FailureDetails TEXT, SourceModule TEXT, SourceId TEXT)", new());
            DbAccessor.ClearTableDefinitionCache();
            UseMail(c => c.HistoryModuleName = "MailHistory");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + "\n" + AddTaskAndSendScript));
            TestContext.Out.WriteLine(ex!.Message);
            Assert.That(ex.Message, Does.Contain("メールは送信しましたが、送信履歴を書けませんでした"));
            Assert.That(ex.Message, Does.Contain("CHECK"));
            Assert.That(_sender.Sent, Has.Count.EqualTo(1));
            Assert.That(await TaskTitlesAsync(), Does.Contain("確定済").And.Not.Contain("未確定"));
            Assert.That(await HistoryAsync(), Is.Empty);
        }

        [Test]
        public async Task 履歴の項目の型が契約と違えば送った後でJobは失敗する()
        {
            //Subject が数値 = 件名を記録できない (送信前の検査は項目の有無だけ)
            var history = _designData.Modules.Find("MailHistory")!;
            history.Fields.RemoveAll(e => e.Name == "Subject");
            history.Fields.Add(new NumberFieldDesign { Name = "Subject", DbColumn = "Subject" });
            UseMail(c => c.HistoryModuleName = "MailHistory");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + "\n" + AddTaskAndSendScript));
            TestContext.Out.WriteLine(ex!.Message);
            Assert.That(ex.Message, Does.Contain("メールは送信しましたが、送信履歴を書けませんでした"));
            Assert.That(ex.Message, Does.Contain("Subject"));
            Assert.That(_sender.Sent, Has.Count.EqualTo(1));
            Assert.That(await TaskTitlesAsync(), Does.Contain("確定済").And.Not.Contain("未確定"));
            Assert.That(await HistoryAsync(), Is.Empty);
        }

        [Test]
        public async Task 履歴モジュールが契約を満たさなければ送らずにJobは失敗する()
        {
            UseMail(c => c.HistoryModuleName = "NoSuchModule");
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + "\n" + AddTaskAndSendScript));
            TestContext.Out.WriteLine(ex!.Message);
            Assert.That(ex.Message, Does.Contain("NoSuchModule"));
            Assert.That(_sender.Sent, Is.Empty);
            Assert.That(await TaskTitlesAsync(), Does.Contain("確定済").And.Not.Contain("未確定"));
        }

        [Test]
        public async Task Fromを入れても使われない_送信インフラのシステム送信者()
        {
            UseMail();
            await RunAsync("""
                void Run(JobArguments args)
                {
                    var m = new MailMessage();
                    m.From = "someone@example.com";
                    m.FromDisplayName = "誰か";
                    m.To.Add("a@example.com");
                    m.Subject = "お知らせ";
                    MailService.Send(m);
                }
                """);
            var sent = _sender.Sent.Single();
            Assert.That(sent.From, Is.Empty);
            Assert.That(sent.FromDisplayName, Is.Empty);
        }

        [Test]
        public async Task DebugRedirectAllToが効く()
        {
            UseMail(c => c.DebugRedirectAllTo = "debug@example.com");
            await RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                }
                """);
            var sent = _sender.Sent.Single();
            Assert.That(sent.To, Is.EqualTo(new[] { "debug@example.com" }));
            Assert.That(sent.Headers[MailDispatcher.OriginalToHeader], Does.Contain("a@example.com"));
        }

        [Test]
        public void MailApiのUseInJobScriptsを呼んでいなければMailServiceを使うJobは失敗する()
        {
            ExcelApi.UseInJobScripts();
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                }
                """));
            TestContext.Out.WriteLine(ex!.Message);
            Assert.That(_sender.Sent, Is.Empty);
        }

        [Test]
        public async Task MailApiのUseInJobScriptsは何度呼んでも登録は1回で設定は後勝ち()
        {
            UseMail(c => c.DebugRedirectAllTo = "first@example.com");
            UseMail(c => c.DebugRedirectAllTo = "second@example.com");
            await RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                }
                """);
            Assert.That(_sender.Sent.Single().To, Is.EqualTo(new[] { "second@example.com" }));
        }

        // ===== Excel =====

        //$記号のテンプレート (Module 版と同じ記法)。A6 は解決できない記号 (そのまま残る)
        static byte[] CreateOrderTemplate()
        {
            using var book = new XLWorkbook();
            var sheet = book.AddWorksheet("Sheet1");
            sheet.Cell(1, 1).SetValue("$Title.Value");
            sheet.Cell(1, 2).SetValue("$Amount.Value");
            sheet.Cell(2, 1).SetValue("$OrderedOn.Value");
            sheet.Cell(2, 2).SetValue("$Customer.Name.Value");
            sheet.Cell(3, 1).SetValue("#LoopRow($Lines.Rows, row)");
            sheet.Cell(3, 2).SetValue("$row.Item.Value");
            sheet.Cell(3, 3).SetValue("$row.Qty.Value");
            sheet.Cell(5, 1).SetValue("$Memo.Value");
            sheet.Cell(6, 1).SetValue("$Nothing.Value");
            using var stream = new MemoryStream();
            book.SaveAs(stream);
            return stream.ToArray();
        }

        const string ExcelMailScript = """
            string Run(JobArguments args)
            {
                var search = new ModuleSearcher<Order>();
                //一覧の行は、読む項目に一覧 (と行を結ぶ Id) を入れたときだけ親と一緒に読まれる
                search.Select(e => e.Id, e => e.Title, e => e.Amount, e => e.OrderedOn, e => e.Customer, e => e.Lines);
                var order = search.ExecuteRawFirstOrDefault();
                var m = new MailMessage();
                m.To.Add("a@example.com");
                m.Subject = "注文書";
                using (var excel = new Excel(Resources.GetMemoryStream("templates/order.xlsx"), "order.xlsx"))
                {
                    excel.OverWrite(order);
                    m.Attachments.Add(excel.ToAttachment("order.xlsx"));
                    m.Attachments.Add(excel.ToPdfAttachment("order.pdf"));
                }
                var r = MailService.Send(m);
                return $"{r.IsSuccess}";
            }
            """;

        [Test]
        public async Task リソースのテンプレートを検索したデータで埋めてxlsxとPDFで添付する_PDFは実変換()
        {
            PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new ArialFontResolver();
            _resources["templates/order.xlsx"] = CreateOrderTemplate();
            UseMail();
            ExcelApi.UseInJobScripts();
            Assert.That(ExcelObject.ConvertPdf, Is.Not.Null);

            Assert.That(await RunAsync(ExcelMailScript), Is.EqualTo("True"));
            var attachments = _sender.Sent.Single().Attachments;
            Assert.That(attachments.Select(e => e.FileName), Is.EqualTo(new[] { "order.xlsx", "order.pdf" }));

            using var book = new XLWorkbook(new MemoryStream(Convert.FromBase64String(attachments[0].ContentBase64)));
            var sheet = book.Worksheet(1);
            Assert.Multiple(() =>
            {
                Assert.That(sheet.Cell(1, 1).GetString(), Is.EqualTo("Order-1"));
                Assert.That(sheet.Cell(1, 2).GetValue<decimal>(), Is.EqualTo(1500m));
                Assert.That(sheet.Cell(2, 1).GetDateTime(), Is.EqualTo(new DateTime(2026, 11, 16)));
                Assert.That(sheet.Cell(2, 2).GetString(), Is.EqualTo("Codeer"));
                //一覧の行の繰り返し (2 行に増える)
                Assert.That(sheet.Cell(3, 2).GetString(), Is.EqualTo("Apple"));
                Assert.That(sheet.Cell(3, 3).GetValue<decimal>(), Is.EqualTo(3m));
                Assert.That(sheet.Cell(4, 2).GetString(), Is.EqualTo("Banana"));
                Assert.That(sheet.Cell(4, 3).GetValue<decimal>(), Is.EqualTo(5m));
                //データに載っていない項目 (DB の列が無い) はデザインにあれば空
                Assert.That(sheet.Cell(6, 1).GetString(), Is.Empty);
                //解決できない記号はそのまま残る (Module 版と同じ)
                Assert.That(sheet.Cell(7, 1).GetString(), Is.EqualTo("$Nothing.Value"));
            });

            var pdf = Convert.FromBase64String(attachments[1].ContentBase64);
            Assert.That(Encoding.ASCII.GetString(pdf, 0, 5), Is.EqualTo("%PDF-"));
        }

        [Test]
        public async Task ToPdfAttachmentはConvertPdfのフックを通る()
        {
            _resources["templates/order.xlsx"] = CreateOrderTemplate();
            UseMail();
            //ExcelApi.UseInJobScripts は設定済みのフックを上書きしない
            var converted = new List<byte[]>();
            ExcelObject.ConvertPdf = stream =>
            {
                converted.Add(stream.ToArray());
                return new MemoryStream(Encoding.ASCII.GetBytes("PDF!"));
            };
            ExcelApi.UseInJobScripts();

            Assert.That(await RunAsync(ExcelMailScript), Is.EqualTo("True"));
            Assert.That(converted, Has.Count.EqualTo(1));
            using (var book = new XLWorkbook(new MemoryStream(converted[0])))
            {
                Assert.That(book.Worksheet(1).Cell(1, 1).GetString(), Is.EqualTo("Order-1"));
            }
            var pdf = _sender.Sent.Single().Attachments[1];
            Assert.That(Encoding.ASCII.GetString(Convert.FromBase64String(pdf.ContentBase64)), Is.EqualTo("PDF!"));
        }

        [Test]
        public void ConvertPdfが無ければToPdfAttachmentは分かる文言で失敗する()
        {
            _resources["templates/order.xlsx"] = CreateOrderTemplate();
            UseMail();
            ExcelApi.UseInJobScripts();
            ExcelObject.ConvertPdf = null;
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(ExcelMailScript));
            Assert.That(ex!.Message, Does.Contain("Excel.ConvertPdf"));
            Assert.That(_sender.Sent, Is.Empty);
        }

        // ===== デザイナの登録 (設計チェック。入力補完が同じ登録表を使うことは本体のテスト) =====

        //本体の JobScriptDesignCheck.Check (internal) をリフレクションで呼ぶ
        List<DesignCheckInfo> DesignCheck(string script)
        {
            _designData.JobScripts["TestJob"] = script;
            var type = typeof(JobExecutor).Assembly.GetType("Codeer.LowCode.Blazor.DesignLogic.Check.JobScriptDesignCheck")!;
            var result = (List<DesignCheckInfo>)type.GetMethod("Check", BindingFlags.Static | BindingFlags.NonPublic, [typeof(DesignData), typeof(string), typeof(string)])!
                .Invoke(null, [_designData, "TestJob", script])!;
            foreach (var e in result) TestContext.Out.WriteLine($"{e.Code}: {e.Message}");
            return result;
        }

        [Test]
        public void デザイナの登録で設計チェックが通る()
        {
            //登録なしでは型が無い
            Assert.That(DesignCheck(ExcelMailScript), Is.Not.Empty);

            JobExecutor.AddScriptTypes(m => Extras.ScriptObjects.JobScriptTypes.Register(m, new Extras.ScriptObjects.MailService()));
            Assert.That(DesignCheck(ExcelMailScript), Is.Empty);
        }

        [Test]
        public void 未結線のMailServiceは分かる文言で失敗する()
        {
            JobExecutor.AddScriptTypes(m => Extras.ScriptObjects.JobScriptTypes.Register(m, new Extras.ScriptObjects.MailService()));
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(SendOneScript + """

                void Run(JobArguments args)
                {
                    Send();
                }
                """));
            Assert.That(ex!.Message, Does.Contain("MailService が結線されていません"));
        }

        [Test]
        public void ExcelApiのUseInJobScriptsを呼んでいなければExcelを使うJobは失敗する()
        {
            _resources["templates/order.xlsx"] = CreateOrderTemplate();
            UseMail();
            Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(ExcelMailScript));
            Assert.That(_sender.Sent, Is.Empty);
        }
    }
}
