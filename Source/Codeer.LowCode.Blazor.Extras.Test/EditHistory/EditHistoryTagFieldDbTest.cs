using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Extras.Test.Tag;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.EditHistory
{
    /// <summary>
    /// 論理削除のモジュールに TagField を置いたとき (タグ付けモジュールは論理削除なし): レコードの論理削除でタグ付け行は DeleteTogether で物理的に消え、
    /// 編集履歴の復活 (削除の版からレコード全体を戻す) が、親と一緒に消えた行の作り直しでタグ付け行を戻す。docs/TagField.md の約束。
    /// </summary>
    public class EditHistoryTagFieldDbTest : IAuthenticationContext
    {
        const string Ds = TagTestDesigns.Ds;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        DesignData _design = null!;
        readonly List<string> _errors = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("7");

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"edit_history_tag_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile};Foreign Keys=False" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE contacts (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT, company_id INTEGER, is_deleted INTEGER)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE contact_tags (id INTEGER PRIMARY KEY AUTOINCREMENT, owner_id INTEGER NOT NULL, name TEXT NOT NULL)", new());
            await _db.ExecuteAsync(Ds, "CREATE UNIQUE INDEX ux_contact_tags_owner_id_name ON contact_tags (owner_id, name)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE edit_histories (id INTEGER PRIMARY KEY AUTOINCREMENT, module_name TEXT, data_id TEXT, change_type TEXT, snapshot TEXT, user_id TEXT, date_time TEXT)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, name) VALUES (7, '石川')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contacts (name) VALUES ('A'), ('B')", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO contact_tags (owner_id, name) VALUES (1, '展示会'), (1, 'DXPO'), (2, '展示会')", new());
            _design = Design();
            _errors.Clear();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        //タグのテストの設計 (Contact + ContactTags) に、Contact の論理削除と編集履歴を足す。ContactTags は論理削除なし (セットアップの形)
        static DesignData Design()
        {
            var d = TagTestDesigns.Create();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            d.AddModule(user);

            var contact = d.Modules.Find("Contact")!;
            contact.Fields.Add(new BooleanFieldDesign { Name = SystemFieldNames.LogicalDelete, DbColumn = "is_deleted" });
            contact.Fields.Add(new EditHistoryFieldDesign { Name = "History", HistoryModuleName = "EditHistory" });

            var history = new ModuleDesign { Name = "EditHistory", DataSourceName = Ds, DbTable = "edit_histories", CanCreate = false, CanUpdate = false };
            history.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            history.Fields.Add(new TextFieldDesign { Name = "ModuleName", DbColumn = "module_name" });
            history.Fields.Add(new TextFieldDesign { Name = "DataId", DbColumn = "data_id" });
            history.Fields.Add(new TextFieldDesign { Name = "ChangeType", DbColumn = "change_type" });
            history.Fields.Add(new TextFieldDesign { Name = "Snapshot", DbColumn = "snapshot" });
            history.Fields.Add(new TextFieldDesign { Name = "UserId", DbColumn = "user_id" });
            history.Fields.Add(new DateTimeFieldDesign { Name = "DateTime", DbColumn = "date_time" });
            history.Fields.Add(new EditHistoryContractFieldDesign { Name = "Contract" });
            history.ListLayouts[""] = new ListLayoutDesign();
            d.AddModule(history);
            return d;
        }

        ModuleDataIO CreateIO()
        {
            var io = new ModuleDataIO(_design, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
            io.AddInterceptor(new EditHistoryRecorder(_design, _errors.Add));
            return io;
        }

        static void AssertNoError(List<ModuleSubmitResult> results)
            => Assert.That(results.Any(e => !string.IsNullOrEmpty(e.ExceptionMessage)), Is.False, string.Join("\n", results.Select(e => e.ExceptionMessage)));

        async Task<string> TagsOfAsync(string ownerId)
            => string.Join(" ", (await _db.QueryAsync(Ds, $"SELECT name FROM contact_tags WHERE owner_id = {ownerId} ORDER BY id", new())).Select(e => e["name"]));

        async Task<object> IsDeletedAsync(string id)
            => (await _db.QueryAsync(Ds, $"SELECT COALESCE(is_deleted, 0) AS d FROM contacts WHERE id = {id}", new())).Single()["d"];

        [Test]
        public async Task 論理削除の親のタグ付け行は物理的に消え_編集履歴の復活で作り直される()
        {
            AssertNoError(await CreateIO().SubmitWithTransactionAsync([new ModuleSubmitData
            {
                ModuleName = "Contact", Id = "1", Delete = [new ModuleDeleteInfo { ModuleName = "Contact", Id = "1" }],
            }]));
            Assert.That(await IsDeletedAsync("1"), Is.EqualTo(1), "親は論理削除");
            Assert.That(await TagsOfAsync("1"), Is.Empty, "タグ付け行は DeleteTogether で物理的に消える");
            Assert.That(await TagsOfAsync("2"), Is.EqualTo("展示会"), "他のレコードのタグは残る");

            //復活ボタンが送る形: 削除の版の履歴行だけ (戻す内容はサーバーが版のスナップショットから組む)
            var histories = await _db.QueryAsync(Ds, "SELECT id, change_type FROM edit_histories ORDER BY id", new());
            var deleteRow = histories.Single(e => (string)e["change_type"] == "Delete");
            var submit = new ModuleSubmitData { ModuleName = "Contact", Id = "1" };
            submit.ExtendedData.Add(new EditHistoryUndeleteData { HistoryModuleName = "EditHistory", HistoryRowId = deleteRow["id"]!.ToString()!, RestoreWholeRecord = true });
            var results = await CreateIO().SubmitWithTransactionAsync([submit]);
            AssertNoError(results);
            Assert.That(results[0].DestinationId, Is.EqualTo("1"), "論理削除の親は同じ Id で戻る");

            Assert.That(await IsDeletedAsync("1"), Is.EqualTo(0));
            Assert.That(await TagsOfAsync("1"), Is.EqualTo("展示会 DXPO"), "タグ付け行は版から作り直される");
            Assert.That((await _db.QueryAsync(Ds, "SELECT change_type FROM edit_histories ORDER BY id", new())).Select(e => e["change_type"]), Is.EqualTo(new[] { "Delete", "Restore" }));
            Assert.That(_errors, Is.Empty);
        }
    }
}
