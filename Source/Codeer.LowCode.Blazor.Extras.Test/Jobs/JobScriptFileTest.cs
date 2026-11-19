using System.Reflection;
using System.Text;
using ClosedXML.Excel;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Excel;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Jobs;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Jobs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using ExcelObject = Codeer.LowCode.Blazor.Extras.ScriptObjects.Excel;

namespace Codeer.LowCode.Blazor.Extras.Test.Jobs
{
    /// <summary>
    /// Job スクリプトの FileService (添付ファイルの読み書き) を、画面と同じ TemporaryFileManager (SQLite の一時ファイル表 + フォルダの置き場所) につないで動かす (JobExecutionContext.FileStorage)。
    /// 登録は static (本体の JobExecutor と MailApi / ExcelApi) なので、テストごとに消して戻す。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class JobScriptFileTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _dir = null!;
        DesignData _designData = null!;
        TemporaryFileManager _files = null!;
        readonly List<MailMessage> _sent = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        class FakeMailSender(List<MailMessage> sent) : IMailSender
        {
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                sent.Add(message);
                return Task.FromResult(MailSendResult.Success(1));
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients) => throw new NotSupportedException();
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
            _sent.Clear();
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"design_jobs_file_test_{Guid.NewGuid():N}.db");
            _dir = Path.Combine(Path.GetTempPath(), $"design_jobs_file_test_{Guid.NewGuid():N}");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE Docs (Id INTEGER PRIMARY KEY, Name TEXT, FileName TEXT, FileSize NUMERIC, FileGuid TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE temporary_files (guid TEXT, created_date_time TEXT)", new());
            _files = new TemporaryFileManager(_db,
                [new TemporaryFileTableInfo { DataSourceName = Ds, Table = "temporary_files", GuidColumn = "guid", CreatedDateTimeColumn = "created_date_time" }],
                new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) });

            _designData = new DesignData();
            var doc = new ModuleDesign { Name = "Doc", DataSourceName = Ds, DbTable = "Docs", CanCreate = true, CanUpdate = true };
            doc.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            doc.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            doc.Fields.Add(new FileFieldDesign { Name = "File", StorageName = "Local", DbColumnFileName = "FileName", DbColumnFileSize = "FileSize", DbColumnFileGuid = "FileGuid" });
            _designData.AddModule(doc);
        }

        [TearDown]
        public void TearDown()
        {
            ClearScriptTypes();
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        Task<string?> RunAsync(string script, Dictionary<string, byte[]>? resources = null)
        {
            _designData.JobScripts["TestJob"] = script;
            return JobScripts.RunAsync(new JobExecutionContext
            {
                ModuleDataIO = new ModuleDataIO(_designData, this, _db, _files),
                DesignData = _designData,
                GetResource = path => resources != null && resources.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null,
                FileStorage = _files,
            }, "TestJob", []);
        }

        async Task<List<IDictionary<string, object>>> DocsAsync() => await _db.QueryAsync(Ds, "SELECT * FROM Docs ORDER BY Id", new());

        string[] StoredFiles() => Directory.Exists(_dir) ? Directory.GetFiles(_dir) : [];

        static readonly Dictionary<string, byte[]> Report = new() { ["report.txt"] = Encoding.UTF8.GetBytes("report") };

        const string AddReportScript = """
            void AddReport()
            {
                var add = new Doc.Data();
                add.Name = new TextFieldData();
                add.Name.Value = "A";
                add.File = FileService.Save("Doc", "File", "report.txt", Resources.GetMemoryStream("report.txt"));
                DBService.Add(add);
            }

            """;

        [Test]
        public async Task Saveして保存すると置き場所に実体が書かれ行に入り一時ファイルでなくなる()
        {
            var message = await RunAsync(AddReportScript + """
                string Run(JobArguments args)
                {
                    AddReport();
                    return "ok";
                }
                """, Report);
            Assert.That(message, Is.EqualTo("ok"));

            var stored = StoredFiles().Single();
            Assert.That(File.ReadAllBytes(stored), Is.EqualTo(Report["report.txt"]));
            var row = (await DocsAsync()).Single();
            var guid = Guid.Parse(row["FileGuid"].ToString()!);
            Assert.That(Path.GetFileName(stored), Is.EqualTo(guid.ToString()));
            Assert.That(row["FileName"], Is.EqualTo("report.txt"));
            Assert.That(await _files.IsTemporaryFileAsync(Ds, guid), Is.False);
        }

        [Test]
        public async Task Saveして保存した後にJobが失敗すると行は入らず実体は一時ファイルとして残る()
        {
            var ex = Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(AddReportScript + """
                string Run(JobArguments args)
                {
                    AddReport();
                    Fail("x");
                    return "x";
                }
                """, Report));
            Assert.That(ex!.Message, Is.EqualTo("x"));

            Assert.That(await DocsAsync(), Is.Empty);
            var stored = StoredFiles().Single();
            Assert.That(await _files.IsTemporaryFileAsync(Ds, Guid.Parse(Path.GetFileName(stored))), Is.True);
        }

        //画面に渡すファイル: 一時ファイルの所有者はジョブの実行ユーザー ("1") でなく起動したユーザー (InvokerUserId)。Guid は結果 (args.Result) で返る
        [Test]
        public async Task SaveTemporaryは起動したユーザーの一時ファイルになり結果で渡せる()
        {
            await _db.ExecuteAsync(Ds, "ALTER TABLE temporary_files ADD COLUMN owner TEXT", new());
            TemporaryFileManager Owned(string userId) => new(_db,
                [new TemporaryFileTableInfo { DataSourceName = Ds, Table = "temporary_files", GuidColumn = "guid", CreatedDateTimeColumn = "created_date_time", OwnerColumn = "owner" }],
                new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) },
                new UserContext(userId));

            var files = Owned("1");
            _designData.JobScripts["TestJob"] = """
                string Run(JobArguments args)
                {
                    var file = FileService.SaveTemporary("Doc", "File", "report.txt", Resources.GetMemoryStream("report.txt"));
                    var result = new JsonObject();
                    result.FileGuid = file.FileGuid;
                    result.FileName = file.FileName;
                    result.FileSize = file.FileSize;
                    args.Result = result;
                    return "ok";
                }
                """;
            var result = await JobExecutor.RunAsync(new JobExecutionContext
            {
                ModuleDataIO = new ModuleDataIO(_designData, this, _db, files),
                DesignData = _designData,
                GetResource = path => Report.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null,
                InvokerUserId = "A",
                FileStorage = files,
            }, "TestJob", [], new ListLogger());
            Assert.That(result.Succeeded, Is.True, result.Error);

            var r = JsonObject.ToJsonObject(result.ResultJson);
            var guid = Guid.Parse((string)r["FileGuid"]!);
            Assert.That(r["FileName"], Is.EqualTo("report.txt"));
            Assert.That(r["FileSize"], Is.EqualTo(6m));
            Assert.That(File.ReadAllBytes(Path.Combine(_dir, guid.ToString())), Is.EqualTo(Report["report.txt"]));
            Assert.That(await Owned("A").IsTemporaryFileAsync(Ds, guid), Is.True, "起動した人の一時ファイル");
            Assert.That(await Owned("1").IsTemporaryFileAsync(Ds, guid), Is.False, "ジョブの実行ユーザーのものではない");
            Assert.That(await DocsAsync(), Is.Empty);
        }

        class UserContext(string userId) : IAuthenticationContext
        {
            public Task<string> GetCurrentUserIdAsync() => Task.FromResult(userId);
        }

        //レコードの添付にあるテンプレート (xlsx) を読み、レコードの値で埋めてメールに添付する
        [Test]
        public async Task GetMemoryStreamで読んだ添付のテンプレートを埋めてメールに添付できる()
        {
            MailApi.UseInJobScripts(new MailConfig { DefaultInfraName = "Main" }, name => name == "Main" ? new FakeMailSender(_sent) : null);
            ExcelApi.UseInJobScripts();

            //テンプレートを添付に持つレコード (置き場所の実体は画面のアップロードと同じ経路で書いて確定する)
            var info = await _files.AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "template.xlsx", new MemoryStream(CreateTemplate()));
            await _files.FixFile(Ds, info.FileGuid);
            await _db.ExecuteAsync(Ds, $"INSERT INTO Docs (Id, Name, FileName, FileSize, FileGuid) VALUES (1, '見積書', 'template.xlsx', {info.FileSize}, '{info.FileGuid}')", new());

            var message = await RunAsync("""
                string Run(JobArguments args)
                {
                    var search = new ModuleSearcher<Doc>();
                    search.AddEquals(e => e.Id.Value, "1");
                    var doc = search.ExecuteRawFirstOrDefault();
                    var template = FileService.GetMemoryStream("Doc", "File", "1");
                    if (template == null) Fail("no template");
                    var mail = new MailMessage();
                    mail.To.Add("a@example.com");
                    mail.Subject = doc.Name.Value;
                    using (var excel = new Excel(template, "template.xlsx"))
                    {
                        excel.OverWrite(doc);
                        mail.Attachments.Add(excel.ToAttachment("out.xlsx"));
                    }
                    return MailService.Send(mail).IsSuccess.ToString();
                }
                """);
            Assert.That(message, Is.EqualTo("True"));

            var attachment = _sent.Single().Attachments.Single();
            Assert.That(attachment.FileName, Is.EqualTo("out.xlsx"));
            using var book = new XLWorkbook(new MemoryStream(Convert.FromBase64String(attachment.ContentBase64)));
            Assert.That(book.Worksheet(1).Cell(1, 1).GetString(), Is.EqualTo("見積書"));
        }

        //PDF 変換はフォントの解決が要る (ホストは起動時に CustomFontResolver を置く)。テストは Windows の Arial で済ませる (JobScriptMailExcelTest と同じ)
        sealed class ArialFontResolver : PdfSharp.Fonts.IFontResolver
        {
            public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"));
            public PdfSharp.Fonts.FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("Arial");
        }

        //テンプレート (リソース) をレコードの値で埋め、xlsx / PDF の内容を FileService.Save で新しいレコードの添付にする
        static string SaveExcelScript(string fileName, string toStream) => $$"""
            string Run(JobArguments args)
            {
                var search = new ModuleSearcher<Doc>();
                search.AddEquals(e => e.Id.Value, "1");
                var doc = search.ExecuteRawFirstOrDefault();
                var add = new Doc.Data();
                add.Name = new TextFieldData();
                add.Name.Value = "帳票";
                using (var excel = new Excel(Resources.GetMemoryStream("template.xlsx"), "template.xlsx"))
                {
                    excel.OverWrite(doc);
                    add.File = FileService.Save("Doc", "File", "{{fileName}}", excel.{{toStream}}());
                }
                DBService.Add(add);
                return "ok";
            }
            """;

        async Task<byte[]> RunSaveExcelAsync(string fileName, string toStream)
        {
            ExcelApi.UseInJobScripts();
            await _db.ExecuteAsync(Ds, "INSERT INTO Docs (Id, Name) VALUES (1, '見積書')", new());
            var message = await RunAsync(SaveExcelScript(fileName, toStream), new() { ["template.xlsx"] = CreateTemplate() });
            Assert.That(message, Is.EqualTo("ok"));

            var row = (await DocsAsync()).Single(e => (string)e["Name"] == "帳票");
            Assert.That(row["FileName"], Is.EqualTo(fileName));
            var guid = Guid.Parse(row["FileGuid"].ToString()!);
            Assert.That(await _files.IsTemporaryFileAsync(Ds, guid), Is.False);
            var stored = StoredFiles().Single();
            Assert.That(Path.GetFileName(stored), Is.EqualTo(guid.ToString()));
            var bytes = File.ReadAllBytes(stored);
            Assert.That(Convert.ToInt64(row["FileSize"]), Is.EqualTo(bytes.Length));
            return bytes;
        }

        [Test]
        public async Task 埋めたExcelをToMemoryStreamでレコードの添付に保存できる()
        {
            var bytes = await RunSaveExcelAsync("x.xlsx", "ToMemoryStream");
            using var book = new XLWorkbook(new MemoryStream(bytes));
            Assert.That(book.Worksheet(1).Cell(1, 1).GetString(), Is.EqualTo("見積書"));
        }

        [Test]
        public async Task 埋めたExcelをToPdfMemoryStreamでPDFにしてレコードの添付に保存できる_PDFは実変換()
        {
            var convertPdf = ExcelObject.ConvertPdf;
            try
            {
                ExcelObject.ConvertPdf = null;
                PdfSharp.Fonts.GlobalFontSettings.FontResolver ??= new ArialFontResolver();
                var bytes = await RunSaveExcelAsync("x.pdf", "ToPdfMemoryStream");
                Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
            }
            finally
            {
                ExcelObject.ConvertPdf = convertPdf;
            }
        }

        static byte[] CreateTemplate()
        {
            using var book = new XLWorkbook();
            book.AddWorksheet("Sheet1").Cell(1, 1).Value = "$Name.Value";
            using var stream = new MemoryStream();
            book.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
