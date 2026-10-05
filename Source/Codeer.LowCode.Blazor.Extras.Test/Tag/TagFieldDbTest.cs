using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.BulkFile;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Excel.Report.PDF;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Tag
{
    /// <summary>
    /// TagField を実 DB (SQLite) で: 検索条件 (タグごとの部分一致 = 本体の Like、すべて含む / いずれかを含む、% を文字として探す) と、
    /// 一括ダウンロード / アップロード (タグの列は保存されている文字列のまま往復し、新しい行もタグ付きで作れる)。
    /// </summary>
    public class TagFieldDbTest : IAuthenticationContext
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("U1");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"tag_field_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, tags TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contacts (name, tags) VALUES ('A', '展示会2026, DXPO'), ('B', '展示会'), ('C', 'セミナー'), ('D', NULL), ('E', '100%達成'), ('F', '')", new());

            _design = new DesignData();
            var m = new ModuleDesign { Name = "Contact", DataSourceName = Ds, DbTable = "contacts" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            m.Fields.Add(new TagFieldDesign { Name = "Tags", DbColumn = "tags" });
            m.ListLayouts[""] = new ListLayoutDesign();
            _design.AddModule(m);
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        ModuleDataIO CreateIO() => new(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));

        static MemoryStream Xlsx(List<List<string>> texts)
        {
            var file = ExcelUtils.CreateExcelBinary(texts, "data");
            file.Position = 0;
            return file;
        }

        //画面の検索欄と同じく TagField に検索の値を入れ、その条件でサーバーの一覧を読む
        async Task<List<string>> NamesAsync(Func<TagField, Task> search)
        {
            var module = await new TestServices(_design).CreateModuleAsync("Contact");
            var field = (TagField)module.GetField("Tags")!;
            await search(field);
            var page = await CreateIO().GetListAsync(new SearchCondition { ModuleName = "Contact", Condition = MultiMatchCondition.And(field.GetMatchCondition()!) }, 0);
            return page.Items.Select(e => ((TextFieldData)e.Fields["Name"]).Value!).OrderBy(e => e).ToList();
        }

        [Test]
        public async Task タグごとの部分一致_展示会で展示会2026も当たる()
            => Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会"])), Is.EqualTo(new[] { "A", "B" }));

        [Test]
        public async Task すべて含むといずれかを含む()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["展示会", "DXPO"])), Is.EqualTo(new[] { "A" }));
            Assert.That(await NamesAsync(async f =>
            {
                await f.SetSearchTagsAsync(["DXPO", "セミナー"]);
                await f.SetSearchMatchAsync(TagSearchMatch.Any);
            }), Is.EqualTo(new[] { "A", "C" }));
        }

        [Test]
        public async Task パーセントは文字として探す()
        {
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["100%"])), Is.EqualTo(new[] { "E" }));
            Assert.That(await NamesAsync(f => f.SetSearchTagsAsync(["%"])), Is.EqualTo(new[] { "E" }));
        }

        [Test]
        public async Task 一括ダウンロードとアップロードでタグの列は文字列のまま往復し新しい行もタグ付きで作れる()
        {
            var file = await BulkFileTransfer.GetListFileAsync(_design, CreateIO(), new SearchCondition { ModuleName = "Contact" });
            file.Position = 0;
            var texts = await ExcelUtils.ReadAllTextsFromExcelBinary(file);
            var column = texts[0].IndexOf("Tags.Value");
            Assert.That(column, Is.GreaterThanOrEqualTo(0), "header: " + string.Join(", ", texts[0]));
            Assert.That(texts.Skip(1).Select(e => e[column]), Does.Contain("展示会2026, DXPO"));

            var result = await BulkFileTransfer.SubmitByFileAsync(_design, CreateIO(), "Contact", Xlsx(
            [
                ["Id.Value", "Name.Value", "Tags.Value"],
                ["", "G", "展示会2027, セミナー"],
            ]));
            Assert.That(result.Select(e => e.ExceptionMessage).Where(e => !string.IsNullOrEmpty(e)), Is.Empty);
            var saved = (await _db.QueryAsync(Ds, "SELECT tags FROM contacts WHERE name = 'G'", new())).Single()["tags"]?.ToString();
            Assert.That(saved, Is.EqualTo("展示会2027, セミナー"));
            Assert.That(TagField.Split(saved), Is.EqualTo(new[] { "展示会2027", "セミナー" }));
        }
    }
}
