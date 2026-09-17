using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Excel.Report.PDF;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.BulkFile
{
    /// <summary>
    /// 標準形式 (内部名ヘッダ xlsx) + FileValueConversionField の一括更新を実 DB (SQLite) で通す。
    /// 参照列 (Link) の空セルは、変換フィールドの有無に関係なく null (DB は NULL) で取り込まれること
    /// (0.12.2 で列マッピング経路に入れた空セル→null と同じ振る舞い) と、名前→Id の引き当てを確認する。
    /// </summary>
    public class FileValueConversionBulkSubmitDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        static DesignData CreateDesign(bool withConversion)
        {
            var d = new DesignData();
            var owners = new ModuleDesign { Name = "Owners", DataSourceName = Ds, DbTable = "owners" };
            owners.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            owners.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            owners.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(owners);

            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "items" };
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            item.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            item.Fields.Add(new LinkFieldDesign { Name = "Owner", DbColumn = "owner_id", SearchCondition = new SearchCondition { ModuleName = "Owners" } });
            if (withConversion)
                item.Fields.Add(new FileValueConversionFieldDesign { Name = "Owner_Conversion", TargetField = "Owner", ExternalField = "Name" });
            item.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(item);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"fvc_bulk_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE owners (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE items (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, owner_id INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO owners (id, name) VALUES (1, '山田'), (2, '佐藤')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO items (id, name, owner_id) VALUES (10, 'A', 1), (11, 'B', 2)", new());
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO(DesignData design) => new(design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        static MemoryStream Xlsx(List<List<string>> texts)
        {
            var file = ExcelUtils.CreateExcelBinary(texts, "data");
            file.Position = 0;
            return file;
        }

        async Task<List<(long Id, string Name, string OwnerType, string? Owner)>> ReadItems()
        {
            var rows = await _db.QueryAsync(Ds, "SELECT id, name, typeof(owner_id) AS t, owner_id FROM items ORDER BY id", new());
            return rows.Select(r => ((long)r["id"], (string)r["name"], (string)r["t"], r["owner_id"]?.ToString())).ToList();
        }

        [Test]
        public async Task 標準形式でも変換フィールドがあれば参照列の空セルはNULLで取り込む()
        {
            //変換フィールドがあるモジュールは型付き経路 (空セル = null)。本体のテキスト経路 (変換フィールド無し) は対象外
            var design = CreateDesign(withConversion: true);
            //既存 10 の参照を外す (Id 付き更新) / 参照なしの新規行
            var result = await BulkFileTransfer.SubmitByFileAsync(design, CreateIO(design), "Item", Xlsx(
            [
                ["Id.Value", "Name.Value", "Owner.Value"],
                ["10", "A2", ""],
                ["", "C", " "],
            ]));
            Assert.That(result.Select(r => r.ExceptionMessage).Where(m => !string.IsNullOrEmpty(m)), Is.Empty);

            var items = await ReadItems();
            Assert.That(items.Select(i => (i.Name, i.OwnerType)), Is.EquivalentTo(new[]
            {
                ("A2", "null"),
                ("B", "integer"),
                ("C", "null"),
            }));
        }

        [Test]
        public async Task 標準形式で参照列を名前で入出力できる()
        {
            var design = CreateDesign(withConversion: true);
            var io = CreateIO(design);

            //ダウンロード: 見出しは Owner.Value のまま、値は名前
            var downloaded = await io.GetTableTextsAsync(new SearchCondition { ModuleName = "Item" });
            downloaded = await FileValueConversionTransform.ToExternalAsync(downloaded, design.Modules.Find("Item")!, io);
            var ownerIndex = downloaded[0].IndexOf("Owner.Value");
            Assert.That(ownerIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(downloaded.Skip(1).Select(r => r[ownerIndex]), Is.EquivalentTo(new[] { "山田", "佐藤" }));

            //取込: 名前 → Id。存在しない名前は行番号付きエラーで全体不採用
            var ng = await BulkFileTransfer.SubmitByFileAsync(design, io, "Item", Xlsx(
            [
                ["Id.Value", "Name.Value", "Owner.Value"],
                ["10", "A", "佐藤"],
                ["11", "B", "鈴木"],
            ]));
            Assert.That(ng[0].ExceptionMessage, Does.Contain("Row 3, Owner.Value: code '鈴木' was not found in 'Owners'."));
            Assert.That((await ReadItems()).First(i => i.Id == 10).Owner, Is.EqualTo("1")); //正常行も入らない

            var ok = await BulkFileTransfer.SubmitByFileAsync(design, io, "Item", Xlsx(
            [
                ["Id.Value", "Name.Value", "Owner.Value"],
                ["10", "A", "佐藤"],
                ["", "C", "山田"],
            ]));
            Assert.That(ok.Select(r => r.ExceptionMessage).Where(m => !string.IsNullOrEmpty(m)), Is.Empty);
            var items = await ReadItems();
            Assert.That(items.Select(i => (i.Name, i.Owner)), Is.EquivalentTo(new[] { ("A", "2"), ("B", "2"), ("C", "1") }));
        }
    }
}
