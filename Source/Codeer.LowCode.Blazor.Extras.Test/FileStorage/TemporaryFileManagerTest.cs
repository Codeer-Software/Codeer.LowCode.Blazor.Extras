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
            await _db.ExecuteAsync(Ds, "CREATE TABLE temporary_files (guid TEXT, created_date_time TEXT)", new());
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
