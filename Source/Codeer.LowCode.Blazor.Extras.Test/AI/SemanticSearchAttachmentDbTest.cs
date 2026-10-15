using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// SemanticSearchService の添付の索引: SourceFields に書いた FileField の添付の中身を、保存時と再索引の文章に入れること。
    /// ストレージ (FileStorages) の有無・対応表に無い拡張子・取り出しの失敗・DB 列格納モード・MaxFileTextLength。
    /// </summary>
    public class SemanticSearchAttachmentDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        const string Storage = "Files";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        FakeEmbeddingProvider _embedding = null!;
        CountingStorage _storage = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        //テンプレートの CustomizedModuleDataIO と同じ結線 (インターセプタの登録)
        sealed class IndexingModuleDataIO : ModuleDataIO
        {
            public IndexingModuleDataIO(DesignData design, IAuthenticationContext auth, IDbAccessor db, ITemporaryFileManager files, SemanticSearchService semanticSearch)
                : base(design, auth, db, files) => AddInterceptor(semanticSearch.Interceptor);
        }

        //読んだ回数を数える置き場所 (ファイルは先に置いておく)
        sealed class CountingStorage(string name) : IFileStorage
        {
            readonly Dictionary<Guid, byte[]> _files = new();
            public string Name => name;
            public int Reads { get; private set; }
            public void Put(Guid file, byte[] content) => _files[file] = content;
            public Task<MemoryStream> ReadAsync(Guid file) { Reads++; return Task.FromResult(new MemoryStream(_files[file], false)); }
            public Task<Stream> OpenReadAsync(Guid file) { Reads++; return Task.FromResult<Stream>(new MemoryStream(_files[file], false)); }
            public Task WriteAsync(Guid file, MemoryStream content) { _files[file] = content.ToArray(); return Task.CompletedTask; }
            public Task DeleteAsync(Guid file) { _files.Remove(file); return Task.CompletedTask; }
        }

        //拡張子 1 つを関数で取り出す実装 (テストで差し込む)
        sealed class DelegateExtractor(string extension, Func<Stream, CancellationToken, Task<string?>> extract) : IFileTextExtractor
        {
            public IEnumerable<string> Extensions => [extension];
            public Task<string?> ExtractAsync(string fileName, Stream content, CancellationToken cancellationToken) => extract(content, cancellationToken);
        }

        sealed class CapturingLoggerFactory : ILoggerFactory
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new();
            public ILogger CreateLogger(string categoryName) => new Logger(Entries);
            public void AddProvider(ILoggerProvider provider) { }
            public void Dispose() { }
            sealed class Logger(List<(LogLevel Level, string Message)> entries) : ILogger
            {
                public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
                public bool IsEnabled(LogLevel logLevel) => true;
                public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                {
                    lock (entries) entries.Add((logLevel, formatter(state, exception)));
                }
            }
        }

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new FileFieldDesign { Name = "Attachment", DisplayName = "添付", StorageName = Storage, DbColumnFileName = "file_name", DbColumnFileGuid = "file_guid" });
            m.Fields.Add(new FileFieldDesign { Name = "Other", DisplayName = "その他", StorageName = Storage, DbColumnFileName = "other_name", DbColumnFileGuid = "other_guid" });
            m.Fields.Add(new FileFieldDesign { Name = "Stored", DisplayName = "DB の添付", DbColumnFileName = "stored_name", DbColumnFileContent = "stored_content" });
            var search = new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = "search_vector" };
            search.SourceFields.AddRange(["Subject", "Attachment", "Stored"]);
            m.Fields.Add(search);
            m.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(m);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_attachment_{Guid.NewGuid():N}.db");
            _db = new([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, file_name TEXT, file_guid TEXT, other_name TEXT, other_guid TEXT, stored_name TEXT, stored_content BLOB, search_text TEXT, search_vector TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject) VALUES ('納期遅れの相談')", new());
            _design = CreateDesign();
            _embedding = new FakeEmbeddingProvider();
            _storage = new CountingStorage(Storage);
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        SemanticSearchService Service(bool withStorages = true, ILoggerFactory? log = null)
            => new(() => _embedding, () => _design, log) { FileStorages = withStorages ? () => [_storage] : null };

        ModuleDataIO CreateIO(SemanticSearchService service) => new IndexingModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()), service);

        async Task<string?> TextAsync(string id)
        {
            var value = (await _db.QueryAsync(Ds, $"SELECT search_text FROM inquiries WHERE id = {id}", new())).Single().Values.Single();
            return value is null or DBNull ? null : value.ToString();
        }

        //ストレージに置いた添付を FileField に付ける更新 (アップロード済みの添付を保存するのと同じ形)
        FileFieldData PutFile(string fileName, string content)
        {
            var guid = Guid.NewGuid();
            _storage.Put(guid, Encoding.UTF8.GetBytes(content));
            return new FileFieldData { FileName = fileName, FileGuid = guid };
        }

        static ModuleSubmitData Update(string id, params (string Field, FieldDataBase Data)[] fields)
        {
            var data = new ModuleData { Name = "Inquiry" };
            data.Fields["Id"] = new IdFieldData { Value = id };
            foreach (var (field, value) in fields) data.Fields[field] = value;
            return new ModuleSubmitData { ModuleName = "Inquiry", Id = id, Update = [data] };
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        [Test]
        public async Task SourceFieldsの添付は中身を読んで文章に入れる()
        {
            AssertNoError(await CreateIO(Service()).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("memo.txt", "\r\n部品 A-100 の欠品について\r\n")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\n添付: memo.txt\n\n添付: memo.txt\n部品 A-100 の欠品について"));
            Assert.That(_storage.Reads, Is.EqualTo(1));
            Assert.That(_embedding.Inputs.Single(), Does.Contain("部品 A-100"), "埋め込みにも先頭 MaxTextLength まで渡る");
        }

        [Test]
        public async Task FileStoragesが無ければファイル名だけでストレージを読まない()
        {
            var log = new CapturingLoggerFactory();
            AssertNoError(await CreateIO(Service(withStorages: false, log)).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("memo.txt", "中身")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\n添付: memo.txt"));
            Assert.That(_storage.Reads, Is.EqualTo(0));
            Assert.That(log.Entries.Where(e => e.Level >= LogLevel.Warning), Is.Empty, "未設定は警告しない");
        }

        [Test]
        public async Task SourceFieldsに無いFileFieldの添付は読まない()
        {
            //Other は SourceFields に無い (Other を書いたこの更新は対象フィールドを書いていないので、索引も作り直さない)
            AssertNoError(await CreateIO(Service()).SubmitWithTransactionAsync([Update("1", ("Subject", new TextFieldData { Value = "件名を変更" }), ("Other", PutFile("other.txt", "その他の中身")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 件名を変更"));
            Assert.That(_storage.Reads, Is.EqualTo(0));
        }

        [Test]
        public async Task DB列格納モードの添付はストレージ無しで行の中身から読む()
        {
            var file = new FileFieldData { FileName = "spec.md", Content = Encoding.UTF8.GetBytes("# 仕様\n防水 IP67") };
            AssertNoError(await CreateIO(Service(withStorages: false)).SubmitWithTransactionAsync([Update("1", ("Stored", file))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\nDB の添付: spec.md\n\nDB の添付: spec.md\n# 仕様\n防水 IP67"));
            Assert.That(_storage.Reads, Is.EqualTo(0));
        }

        [Test]
        public async Task 対応表に無い拡張子の添付はストレージを読まない()
        {
            AssertNoError(await CreateIO(Service()).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("old.doc", "旧形式")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\n添付: old.doc"));
            Assert.That(_storage.Reads, Is.EqualTo(0));

            //外した拡張子も同じ。足した拡張子は読む
            var service = Service();
            service.FileTextExtractors.Remove(".txt");
            service.FileTextExtractors.Add(new DelegateExtractor(".log", async (content, ct) => "ログ: " + await new StreamReader(content).ReadToEndAsync(ct)));
            AssertNoError(await CreateIO(service).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("memo.txt", "中身")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\n添付: memo.txt"));
            Assert.That(_storage.Reads, Is.EqualTo(0));
            AssertNoError(await CreateIO(service).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("app.log", "起動しました")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談\n添付: app.log\n\n添付: app.log\nログ: 起動しました"));
            Assert.That(_storage.Reads, Is.EqualTo(1));
        }

        [Test]
        public async Task 取り出しが例外なら保存は成功し中身なしで警告ログを出す()
        {
            var log = new CapturingLoggerFactory();
            var service = Service(log: log);
            service.FileTextExtractors.Add(new DelegateExtractor(".txt", (content, ct) => throw new InvalidDataException("壊れたファイル (fake)")));
            AssertNoError(await CreateIO(service).SubmitWithTransactionAsync([Update("1", ("Subject", new TextFieldData { Value = "件名を変更" }), ("Attachment", PutFile("broken.txt", "x")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 件名を変更\n添付: broken.txt"), "保存は通り、文章はファイル名だけ");
            var warning = log.Entries.Single(e => e.Level == LogLevel.Warning);
            Assert.That(warning.Message, Does.StartWith("SemanticSearch: ").And.Contain("Inquiry").And.Contain("Attachment").And.Contain("broken.txt"));

            //置き場所の名前が合わない (読めない) のも同じ
            _design.Modules.Find("Inquiry")!.Fields.OfType<FileFieldDesign>().First(f => f.Name == "Attachment").StorageName = "Missing";
            AssertNoError(await CreateIO(Service(log: log)).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("memo.txt", "中身")))]));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 件名を変更\n添付: memo.txt"));
            Assert.That(log.Entries.Count(e => e.Level == LogLevel.Warning), Is.EqualTo(2));
        }

        [Test]
        public async Task 添付の中身はMaxFileTextLengthで切る_文章の列は行全体なら全文()
        {
            var field = _design.Modules.Find("Inquiry")!.Fields.OfType<SemanticSearchFieldDesign>().Single();
            field.MaxFileTextLength = 6;
            field.MaxTextLength = 10;
            AssertNoError(await CreateIO(Service()).SubmitWithTransactionAsync([Update("1", ("Attachment", PutFile("memo.txt", "0123456789")))]));
            var text = await TextAsync("1");
            Assert.That(text, Is.EqualTo("件名: 納期遅れの相談\n添付: memo.txt\n\n添付: memo.txt\n012345"));
            Assert.That(_embedding.Inputs.Single(), Is.EqualTo(text![..10]), "埋め込みは行全体で MaxTextLength まで");
        }

        [Test]
        public async Task 再索引で行から作る文章にも添付の中身が入る()
        {
            var file = PutFile("memo.txt", "再索引で読む中身");
            await _db.ExecuteAsync(Ds, "INSERT INTO inquiries (subject, file_name, file_guid) VALUES ('取込の行', 'memo.txt', @p1)", new() { ["@p1"] = file.FileGuid!.Value.ToString() });
            var service = Service();
            var io = CreateIO(service);
            Assert.That(await service.ReindexAsync(io, _db, "Inquiry", missingOnly: true), Is.EqualTo(2));
            Assert.That(await TextAsync("1"), Is.EqualTo("件名: 納期遅れの相談"));
            Assert.That(await TextAsync("2"), Is.EqualTo("件名: 取込の行\n添付: memo.txt\n\n添付: memo.txt\n再索引で読む中身"));
            Assert.That(_storage.Reads, Is.EqualTo(1));

            //文章がある行 (ベクトルだけ無い行) は文章をそのまま埋め込む = 添付も読み直さない
            await _db.ExecuteAsync(Ds, "UPDATE inquiries SET search_vector = NULL", new());
            Assert.That(await service.ReindexAsync(io, _db, "Inquiry", missingOnly: true), Is.EqualTo(2));
            Assert.That(_storage.Reads, Is.EqualTo(1));
        }
    }
}
