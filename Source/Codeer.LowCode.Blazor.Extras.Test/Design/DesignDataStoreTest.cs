using Codeer.LowCode.Blazor.Extras.Server.Design;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Codeer.LowCode.Blazor.Extras.Test.Design
{
    /// <summary>
    /// DesignDataStore: DesignFileDirectory の App.zip を読み込んで持つ。
    /// App.zip が無ければ空のデザイン・空の Version、置けば読み込んで Version = App.zip の SHA-256 (小文字 hex)、
    /// 変わっていなければ同じインスタンスを返し、差し替われば読み直して onReloaded を呼ぶ。
    /// </summary>
    public class DesignDataStoreTest
    {
        //リポジトリのサンプルデザイン (差し替えの前後で中身を変えるときは一部のモジュールを除く)
        static string ExampleDesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "Example", "Design"));

        string _dir = string.Empty;
        int _reloaded;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), $"design_store_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_dir);
            _reloaded = 0;
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        string ZipPath => Path.Combine(_dir, "App.zip");

        DesignDataStore CreateStore() => new(_dir, () => _reloaded++);

        //サンプルデザインを App.zip にして置く。excludeModule を指定するとそのモジュールのファイルを除く (差し替え後の中身を変える)
        void PlaceZip(DateTime lastWriteTime, string? excludeModule = null)
        {
            var work = Path.Combine(_dir, "work");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            CopyDirectory(ExampleDesignDir, work);
            if (excludeModule != null)
            {
                foreach (var file in Directory.GetFiles(Path.Combine(work, "Modules"), excludeModule + ".*", SearchOption.AllDirectories)) File.Delete(file);
            }
            var temp = Path.Combine(_dir, "App.zip.tmp");
            if (File.Exists(temp)) File.Delete(temp);
            ZipFile.CreateFromDirectory(work, temp);
            File.Move(temp, ZipPath, true);
            File.SetLastWriteTime(ZipPath, lastWriteTime);
            Directory.Delete(work, true);
        }

        static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        string ZipSha256()
        {
            using var stream = File.OpenRead(ZipPath);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        [Test]
        public void AppZipが無ければ空のデザインで版も空()
        {
            var current = CreateStore().GetCurrent();
            Assert.That(current.Version, Is.Empty);
            Assert.That(current.DesignData.Modules.GetModuleNames(), Is.Empty);
        }

        [Test]
        public void AppZipが無いまま繰り返し取っても読み直さない()
        {
            var store = CreateStore();
            var first = store.GetCurrent();
            var second = store.GetCurrent();
            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(_reloaded, Is.EqualTo(0));
        }

        [Test]
        public void AppZipが消えると空のデザインに戻しonReloadedを1回だけ呼ぶ()
        {
            PlaceZip(new DateTime(2026, 1, 1, 10, 0, 0));
            var store = CreateStore();
            var loaded = store.GetCurrent();
            Assert.That(_reloaded, Is.EqualTo(1));

            File.Delete(ZipPath);
            var empty = store.GetCurrent();
            Assert.That(ReferenceEquals(loaded, empty), Is.False);
            Assert.That(empty.Version, Is.Empty);
            Assert.That(empty.DesignData.Modules.GetModuleNames(), Is.Empty);
            Assert.That(_reloaded, Is.EqualTo(2));

            //消えたまま繰り返し取っても読み直さない
            Assert.That(ReferenceEquals(empty, store.GetCurrent()), Is.True);
            Assert.That(ReferenceEquals(empty, store.GetCurrent()), Is.True);
            Assert.That(_reloaded, Is.EqualTo(2));
        }

        [Test]
        public void AppZipを置くと読み込まれ版はSHA256の小文字hex()
        {
            PlaceZip(new DateTime(2026, 1, 1, 10, 0, 0));
            var store = CreateStore();
            var current = store.GetCurrent();

            Assert.That(current.DesignData.Modules.Find("AppUser"), Is.Not.Null);
            Assert.That(current.Version, Is.EqualTo(ZipSha256()));
            Assert.That(current.Version, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(store.GetDesignData(), Is.SameAs(current.DesignData));
            Assert.That(_reloaded, Is.EqualTo(1));
        }

        [Test]
        public void 変わっていなければ同じインスタンスを返す()
        {
            PlaceZip(new DateTime(2026, 1, 1, 10, 0, 0));
            var store = CreateStore();
            var first = store.GetCurrent();
            var second = store.GetCurrent();
            var third = store.GetCurrent();

            Assert.That(ReferenceEquals(first, second), Is.True);
            Assert.That(ReferenceEquals(second, third), Is.True);
            Assert.That(_reloaded, Is.EqualTo(1));
        }

        [Test]
        public void 差し替えると読み直してonReloadedを呼ぶ()
        {
            PlaceZip(new DateTime(2026, 1, 1, 10, 0, 0));
            var store = CreateStore();
            var before = store.GetCurrent();
            Assert.That(before.DesignData.Modules.Find("ExpenseRequest"), Is.Not.Null);

            PlaceZip(new DateTime(2026, 1, 1, 11, 0, 0), excludeModule: "ExpenseRequest");
            var after = store.GetCurrent();

            Assert.That(ReferenceEquals(before, after), Is.False);
            Assert.That(after.DesignData.Modules.Find("ExpenseRequest"), Is.Null, "差し替え後の中身で読み直している");
            Assert.That(after.Version, Is.EqualTo(ZipSha256()));
            Assert.That(after.Version, Is.Not.EqualTo(before.Version));
            Assert.That(_reloaded, Is.EqualTo(2));

            //読み直した後は再び同じインスタンス
            Assert.That(ReferenceEquals(after, store.GetCurrent()), Is.True);
            Assert.That(_reloaded, Is.EqualTo(2));
        }
    }
}
