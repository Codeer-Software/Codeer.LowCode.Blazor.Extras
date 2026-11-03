using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>アップロードの本文を読みながら置き場所へ書く経路。成功なら一時行と実体と大きさ、途中で失敗したら一時行も途中までの実体も残さない。</summary>
    public class TemporaryFileManagerTest
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        string _dir = string.Empty;
        DbAccessor _db = default!;

        [SetUp]
        public async Task SetUp()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), $"clb_tmpfile_{Guid.NewGuid():N}.db");
            _dir = Path.Combine(Path.GetTempPath(), "clb_tmpfile_" + Guid.NewGuid().ToString("N"));
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE temporary_files (guid TEXT, created_date_time TEXT, owner TEXT)", new());
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            SqliteTestDb.Delete(_dbFile);
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        TemporaryFileManager Manager() => new(_db,
            [new TemporaryFileTableInfo { DataSourceName = Ds, Table = "temporary_files", GuidColumn = "guid", CreatedDateTimeColumn = "created_date_time" }],
            new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) });

        TemporaryFileManager ManagerWithOwner(string? userId) => new(_db,
            [new TemporaryFileTableInfo { DataSourceName = Ds, Table = "temporary_files", GuidColumn = "guid", CreatedDateTimeColumn = "created_date_time", OwnerColumn = "owner" }],
            new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) },
            new FakeAuthentication(userId));

        //所有者列の設定は無いが、ユーザーは渡すホスト
        TemporaryFileManager ManagerWithoutOwnerColumn(string? userId) => new(_db,
            [new TemporaryFileTableInfo { DataSourceName = Ds, Table = "temporary_files", GuidColumn = "guid", CreatedDateTimeColumn = "created_date_time" }],
            new List<IFileStorage> { new FileSystemFileStorage(new FileSystemStorageSettings { Name = "Local", Directory = _dir }) },
            new FakeAuthentication(userId));

        class FakeAuthentication(string? userId) : IAuthenticationContext
        {
            public Task<string> GetCurrentUserIdAsync() => Task.FromResult(userId ?? string.Empty);
        }

        async Task<int> RowCount() => (await _db.QueryAsync(Ds, "select count(*) as c from temporary_files", new())).Count == 0 ? 0
            : Convert.ToInt32((await _db.QueryAsync(Ds, "select count(*) as c from temporary_files", new()))[0]["c"]);

        [Test]
        public async Task 本文を流しながら書いて大きさを数える()
        {
            var info = await Manager().AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.mp4", new ForwardOnly(new byte[70000], failAt: -1));
            Assert.That(info.FileSize, Is.EqualTo(70000));
            Assert.That(new System.IO.FileInfo(Path.Combine(_dir, info.FileGuid.ToString()!)).Length, Is.EqualTo(70000));
            Assert.That(await RowCount(), Is.EqualTo(1));
        }

        [Test]
        public async Task 途中で切れたら一時行も途中までの実体も残さない()
        {
            var manager = Manager();
            Assert.ThrowsAsync<IOException>(() => manager.AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.mp4", new ForwardOnly(new byte[70000], failAt: 20000)));
            Assert.That(await RowCount(), Is.EqualTo(0));
            Assert.That(Directory.Exists(_dir) ? Directory.GetFiles(_dir) : [], Is.Empty);
        }

        [Test]
        public async Task 一時ファイルかどうかは一時行の有無()
        {
            var manager = Manager();
            var info = await manager.AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream(new byte[10]));
            Assert.That(await manager.IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.True);
            Assert.That(await manager.IsTemporaryFileAsync(Ds, Guid.NewGuid()), Is.False);
            await manager.FixFile(Ds, info.FileGuid);
            Assert.That(await manager.IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.False);
        }

        [Test]
        public async Task 複製は同じ中身の新しい一時ファイル()
        {
            var manager = Manager();
            var src = await manager.AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream([1, 2, 3]));
            await manager.FixFile(Ds, src.FileGuid);

            var copied = await manager.CopyFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, src.FileGuid!.Value);
            Assert.That(copied, Is.Not.EqualTo(src.FileGuid));
            Assert.That(File.ReadAllBytes(Path.Combine(_dir, copied.ToString())), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(await manager.IsTemporaryFileAsync(Ds, copied), Is.True, "行に入るまでは一時ファイル");
            Assert.That(await manager.IsTemporaryFileAsync(Ds, src.FileGuid!.Value), Is.False, "元はそのまま");
        }

        [Test]
        public async Task 所有者列があればアップロードした人を記録し同じ人だけ一時ファイルと認める()
        {
            var info = await ManagerWithOwner("A").AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream(new byte[10]));
            Assert.That(await ManagerWithOwner("A").IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.True);
            Assert.That(await ManagerWithOwner("B").IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.False, "他の人のアップロード");
            Assert.That(await ManagerWithOwner("A").IsTemporaryFileAsync(Ds, Guid.NewGuid()), Is.False);
        }

        [Test]
        public async Task 複製も複製した人のもの()
        {
            var manager = ManagerWithOwner("A");
            var src = await manager.AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream([1, 2, 3]));
            await manager.FixFile(Ds, src.FileGuid);

            var copied = await manager.CopyFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, src.FileGuid!.Value);
            Assert.That(await ManagerWithOwner("A").IsTemporaryFileAsync(Ds, copied), Is.True);
            Assert.That(await ManagerWithOwner("B").IsTemporaryFileAsync(Ds, copied), Is.False);
        }

        [Test]
        public async Task 所有者列があってもユーザーが無ければ所有者null同士で認める()
        {
            var info = await ManagerWithOwner("").AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream(new byte[10]));
            Assert.That(await ManagerWithOwner("").IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.True);
            Assert.That(await ManagerWithOwner("A").IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.False);
        }

        [Test]
        public async Task 所有者列の設定が無ければ誰でも認める()
        {
            var info = await Manager().AddFileAsync(new FileSaveInfo { DataSourceName = Ds, StorageName = "Local" }, "a.txt", new MemoryStream(new byte[10]));
            Assert.That(await ManagerWithoutOwnerColumn("B").IsTemporaryFileAsync(Ds, info.FileGuid!.Value), Is.True);
        }

        //Request.Body の性質 (戻れない・長さ不明)。failAt を過ぎると切断のように例外を投げる
        class ForwardOnly(byte[] data, int failAt) : Stream
        {
            int _position;
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count)
            {
                if (failAt >= 0 && _position >= failAt) throw new IOException("connection reset");
                var n = Math.Min(count, data.Length - _position);
                if (failAt >= 0) n = Math.Min(n, failAt - _position);
                Array.Copy(data, _position, buffer, offset, n);
                _position += n;
                return n;
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
