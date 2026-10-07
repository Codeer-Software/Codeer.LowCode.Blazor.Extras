using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    public class TagSetupServiceTest : SetupTestBase, IAuthenticationContext
    {
        static readonly Dictionary<string, List<DbTableDefinition>> NoTables = new();

        public Task<string> GetCurrentUserIdAsync() => Task.FromResult("1");

        static TagSetupOptions Options(string target = "Request") => new() { TargetModuleName = target, DataSourceName = "Main" };

        static List<DesignCheckInfo> Check(DesignData d, string module, string field)
            => d.Modules.Find(module)!.Fields.Single(e => e.Name == field).CheckDesign(new DesignCheckContext(module, d, NoTables));

        [Test]
        public void タグ付けを作りTagFieldを結び付けデザインチェックを通る()
        {
            CreateFixture();
            var result = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite);
            Assert.That(result.CreatedModules, Is.EqualTo(new[] { "RequestTags" }), "マスタは作らない");

            var d = Load();
            Assert.That(d.Modules.Find("Tag"), Is.Null);
            var link = d.Modules.Find("RequestTags")!;
            Assert.That(link.DbTable, Is.EqualTo("request_tags"));
            Assert.That(((IdFieldDesign)link.Fields.Single(e => e.Name == "OwnerId")).DbColumn, Is.EqualTo("owner_id"));
            var name = (TextFieldDesign)link.Fields.Single(e => e.Name == "Name");
            Assert.That(name.DbColumn, Is.EqualTo("name"));
            Assert.That(name.MaxLength, Is.EqualTo(TagField.MaxTagLength), "入力欄・DDL と同じ長さ");
            Assert.That(link.Fields.OfType<LinkFieldDesign>(), Is.Empty);

            var field = d.Modules.Find("Request")!.Fields.OfType<TagFieldDesign>().Single();
            Assert.That(field.Name, Is.EqualTo("Tags"));
            Assert.That(field.SearchCondition.ModuleName, Is.EqualTo("RequestTags"));

            Assert.That(Check(d, "Request", "Tags"), Is.Empty);
            Assert.That(Check(d, "RequestTags", "TagLinkContract"), Is.Empty);
        }

        [Test]
        public void DDLは一意インデックスと名前のインデックスと外部キーをDBごとに()
        {
            CreateFixture();
            var sqlite = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite).Ddl;
            TestContext.Out.WriteLine(string.Join("\n", sqlite));
            Assert.That(sqlite.Count(e => e.StartsWith("CREATE TABLE")), Is.EqualTo(1));
            Assert.That(sqlite.Select(e => e.Trim()), Does.Contain("owner_id INTEGER NOT NULL REFERENCES requests (id) ON DELETE CASCADE,"), "SQLite は列に外部キー");
            Assert.That(sqlite.Select(e => e.Trim()), Does.Contain("name TEXT NOT NULL"), "照合順序は付けない (完全一致)");
            Assert.That(sqlite, Does.Contain("CREATE UNIQUE INDEX ux_request_tags_owner_id_name ON request_tags (owner_id, name);"));
            Assert.That(sqlite, Does.Contain("CREATE INDEX ix_request_tags_name ON request_tags (name);"));
            Assert.That(sqlite.Any(e => e.Contains("ADD CONSTRAINT")), Is.False);

            //長さはタグ名の最大の長さ (TagField.MaxTagLength) と同じ
            var expected = new Dictionary<DataSourceType, string>
            {
                //既定の照合順序が大文字小文字を区別しない DB には、区別する照合順序を付ける (完全一致)
                [DataSourceType.SQLServer] = $"name NVARCHAR({TagField.MaxTagLength}) COLLATE Latin1_General_CS_AS_KS_WS NOT NULL",
                [DataSourceType.MySQL] = $"name VARCHAR({TagField.MaxTagLength}) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL",
                [DataSourceType.Oracle] = $"name VARCHAR2({TagField.MaxTagLength}) NOT NULL",
                [DataSourceType.PostgreSQL] = $"name VARCHAR({TagField.MaxTagLength}) NOT NULL",
            };
            foreach (var (type, name) in expected)
            {
                SetUpProjectDir();
                CreateFixture();
                var ddl = TagSetupService.Run(Load(), ProjectDir, Options(), type).Ddl;
                TestContext.Out.WriteLine(string.Join("\n", ddl));
                Assert.That(ddl.Select(e => e.Trim().TrimEnd(',')), Does.Contain(name), type.ToString());
                Assert.That(ddl, Does.Contain("CREATE UNIQUE INDEX ux_request_tags_owner_id_name ON request_tags (owner_id, name);"), type.ToString());
                Assert.That(ddl, Does.Contain("CREATE INDEX ix_request_tags_name ON request_tags (name);"), type.ToString());
                Assert.That(ddl, Does.Contain("ALTER TABLE request_tags ADD CONSTRAINT fk_request_tags_owner_id FOREIGN KEY (owner_id) REFERENCES requests (id) ON DELETE CASCADE;"), type.ToString());
                Assert.That(ddl.Any(e => e.Contains("citext", StringComparison.OrdinalIgnoreCase) || e.Contains("UPPER(") || e.Contains("lower(")), Is.False, type + ": 式インデックス・拡張は使わない");
                Assert.That(ddl.Any(e => e.Contains("COLLATE")), Is.EqualTo(type is DataSourceType.SQLServer or DataSourceType.MySQL), type + ": 照合順序は既定が区別しない DB だけ");
            }
        }

        [Test]
        public void モジュールごとにタグ付けを作りもう一度なら何もしない()
        {
            CreateFixture();
            TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite);
            var other = new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customers" };
            other.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            SaveModule(other);

            var result = TagSetupService.Run(Load(), ProjectDir, Options("Customer"), DataSourceType.SQLite);
            Assert.That(result.CreatedModules, Is.EqualTo(new[] { "CustomerTags" }));
            Assert.That(result.Ddl.Count(e => e.StartsWith("CREATE TABLE")), Is.EqualTo(1));
            Assert.That(Check(Load(), "Customer", "Tags"), Is.Empty);

            //もう一度: 何も作らず、結び付き済みの TagField も触らない
            var again = TagSetupService.Run(Load(), ProjectDir, Options("Customer"), DataSourceType.SQLite);
            Assert.That(again.CreatedModules, Is.Empty);
            Assert.That(again.SkippedModules, Is.EqualTo(new[] { "CustomerTags" }));
            Assert.That(again.Ddl, Is.Empty);
        }

        [Test]
        public void 結び付きなしの同名TagFieldは結び付けフォルダ分けのモジュールはその場で保存する()
        {
            CreateFixture();
            //結び付きの無い TagField (検索条件が空) が、Modules のサブフォルダにある
            var contact = new ModuleDesign { Name = "Contact", DataSourceName = "Main", DbTable = "contacts" };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new TagFieldDesign { Name = "タグ", DisplayName = "タグ", Placeholder = "タグを入力", ConfirmOnSpace = true });
            Directory.CreateDirectory(Path.Combine(ProjectDir, "Modules", "SFA"));
            WriteFile(Path.Combine("Modules", "SFA", "Contact.mod.json"), JsonConverterEx.SerializeObject(contact));

            var options = Options("Contact");
            options.FieldName = "タグ";
            options.LinkModuleName = "ContactTag";
            options.LinkTableName = "contact_tags";
            TagSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "Contact.mod.json")), Is.False);
            var d = Load();
            var fields = d.Modules.Find("Contact")!.Fields;
            var field = fields.OfType<TagFieldDesign>().Single();
            Assert.That((field.Name, field.Placeholder, field.ConfirmOnSpace, field.SearchCondition.ModuleName), Is.EqualTo(("タグ", "タグを入力", true, "ContactTag")));
            Assert.That(fields.Select(e => e.Name), Is.EqualTo(new[] { "Id", "タグ" }), "同じ位置");
            Assert.That(d.Modules.Find("ContactTag")!.DbTable, Is.EqualTo("contact_tags"));
            Assert.That(Check(d, "Contact", "タグ"), Is.Empty);
        }

        [Test]
        public void タグ付けのOwnerIdの列はIdの型でNOTNULL()
        {
            foreach (var (type, idType) in new[] { (DataSourceType.SQLServer, "BIGINT"), (DataSourceType.PostgreSQL, "BIGINT"), (DataSourceType.MySQL, "BIGINT"), (DataSourceType.Oracle, "NUMBER"), (DataSourceType.SQLite, "INTEGER") })
            {
                SetUpProjectDir();
                CreateFixture();
                var ddl = TagSetupService.Run(Load(), ProjectDir, Options(), type).Ddl.Select(e => e.Trim()).ToList();
                Assert.That(ddl.Any(e => e.StartsWith($"owner_id {idType} NOT NULL")), Is.True, type + ": " + string.Join(" / ", ddl));
            }
        }

        [Test]
        public void テーブルの無いモジュールには付けられない()
        {
            CreateFixture();
            SaveModule(new ModuleDesign { Name = "Screen" });
            Assert.Throws<InvalidOperationException>(() => TagSetupService.Run(Load(), ProjectDir, Options("Screen"), DataSourceType.SQLite));
        }

        [Test]
        public async Task 生成したデザインとDDLでタグを保存でき消せば一緒に消える()
        {
            CreateFixture();
            var ddl = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite).Ddl;
            var design = Load();
            //固定のユーザーモジュール (Example の AppUser) は別のデータソース。このテストはユーザーを使わない
            design.AppSettings.CurrentUserModuleDesignName = string.Empty;
            //セットアップの次の手順 (ユーザーが行う): 詳細レイアウトに TagField を置く
            design.Modules.Find("Request")!.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Title", "Tags" } };

            var dbFile = Path.Combine(Path.GetTempPath(), $"tag_setup_{Guid.NewGuid():N}.db");
            var db = new DbAccessor([new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={dbFile};Foreign Keys=True" }]);
            try
            {
                DbAccessor.ClearTableDefinitionCache();
                await db.ExecuteAsync("Main", "CREATE TABLE requests (id INTEGER PRIMARY KEY AUTOINCREMENT, title TEXT)", new());
                await db.ExecuteAsync("Main", "INSERT INTO requests (title) VALUES ('R1')", new());
                //DDL は 1 要素 1 行 (CREATE TABLE は複数行)。セットアップの結果画面・CLI と同じく、つないで 1 回で流す
                await db.ExecuteAsync("Main", string.Join("\n", ddl), new());

                var client = new DbClientServices(design, () => new ModuleDataIO(design, this, db, new TemporaryFileManager(db, [], new List<IFileStorage>())));
                var module = await client.OpenAsync("Request", "1");
                var tags = (TagField)module.GetField("Tags")!;
                await tags.AddTagAsync("展示会, DXPO");
                Assert.That(await module.SubmitAsync(), Is.True, string.Join(" | ", client.Logger.ErrorList));

                var reopened = (TagField)(await client.OpenAsync("Request", "1")).GetField("Tags")!;
                Assert.That(reopened.Tags, Is.EqualTo(new[] { "展示会", "DXPO" }));
                Assert.That(await reopened.GetCandidatesAsync("DX"), Is.EqualTo(new[] { "DXPO" }));

                //同じレコードに同じタグをもう 1 行作ろうとしても一意インデックスが止める
                Assert.ThrowsAsync<SqliteException>(async () => await db.ExecuteAsync("Main", "INSERT INTO request_tags (owner_id, name) VALUES (1, 'DXPO')", new()));

                //レコードを DB で直接消しても外部キーでタグ付けが消える
                await db.ExecuteAsync("Main", "DELETE FROM requests WHERE id = 1", new());
                Assert.That((await db.QueryAsync("Main", "SELECT COUNT(*) AS n FROM request_tags", new())).Single()["n"], Is.EqualTo(0L));
            }
            finally
            {
                await db.DisposeAsync();
                SqliteConnection.ClearAllPools();
                if (File.Exists(dbFile)) File.Delete(dbFile);
            }
        }
    }
}
