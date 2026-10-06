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
using Codeer.LowCode.Blazor.Repository.Match;
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
        public void マスタとタグ付けを作りTagFieldを結び付けデザインチェックを通る()
        {
            CreateFixture();
            var result = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite);
            Assert.That(result.CreatedModules, Is.EqualTo(new[] { "Tag", "RequestTags" }));

            var d = Load();
            var master = d.Modules.Find("Tag")!;
            Assert.That(master.DbTable, Is.EqualTo("tags"));
            var link = d.Modules.Find("RequestTags")!;
            Assert.That(link.DbTable, Is.EqualTo("request_tags"));
            Assert.That(((IdFieldDesign)link.Fields.Single(e => e.Name == "OwnerId")).DbColumn, Is.EqualTo("request_id"));

            var field = d.Modules.Find("Request")!.Fields.OfType<TagFieldDesign>().Single();
            Assert.That(field.Name, Is.EqualTo("Tags"));
            Assert.That(field.SearchCondition.ModuleName, Is.EqualTo("RequestTags"));

            Assert.That(Check(d, "Request", "Tags"), Is.Empty);
            Assert.That(Check(d, "Tag", "TagContract"), Is.Empty);
            Assert.That(Check(d, "RequestTags", "TagLinkContract"), Is.Empty);
            Assert.That(Check(d, "RequestTags", "Tag"), Is.Empty);

            //マスタの画面のリンク
            Assert.That(d.PageFrames.Find("Main")!.Left.Links.Select(e => e.Module), Does.Contain("Tag"));
        }

        [Test]
        public void DDLはテーブルと一意インデックスとSQLite以外は外部キー()
        {
            CreateFixture();
            var sqlite = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite).Ddl;
            TestContext.Out.WriteLine(string.Join("\n", sqlite));
            Assert.That(sqlite.Count(e => e.StartsWith("CREATE TABLE")), Is.EqualTo(2));
            Assert.That(sqlite, Does.Contain("CREATE UNIQUE INDEX ux_tags_name ON tags (name COLLATE NOCASE);"));
            Assert.That(sqlite, Does.Contain("CREATE UNIQUE INDEX ux_request_tags_request_id_tag_id ON request_tags (request_id, tag_id);"));
            Assert.That(sqlite, Does.Contain("CREATE INDEX ix_request_tags_tag_id ON request_tags (tag_id);"));
            Assert.That(sqlite.Any(e => e.Contains("FOREIGN KEY")), Is.False);

            SetUpProjectDir();
            CreateFixture();
            var sqlServer = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLServer).Ddl;
            TestContext.Out.WriteLine(string.Join("\n", sqlServer));
            Assert.That(sqlServer, Does.Contain("ALTER TABLE tags ALTER COLUMN name NVARCHAR(200) NOT NULL;"));
            Assert.That(sqlServer, Does.Contain("ALTER TABLE request_tags ADD CONSTRAINT fk_request_tags_request_id FOREIGN KEY (request_id) REFERENCES requests (id);"));
            Assert.That(sqlServer, Does.Contain("ALTER TABLE request_tags ADD CONSTRAINT fk_request_tags_tag_id FOREIGN KEY (tag_id) REFERENCES tags (id);"));
        }

        [Test]
        public void 二つ目のモジュールはマスタを使いまわす()
        {
            CreateFixture();
            TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite);
            var other = new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customers" };
            other.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            SaveModule(other);

            var result = TagSetupService.Run(Load(), ProjectDir, Options("Customer"), DataSourceType.SQLite);
            Assert.That(result.CreatedModules, Is.EqualTo(new[] { "CustomerTags" }));
            Assert.That(result.SkippedModules, Is.EqualTo(new[] { "Tag" }));
            Assert.That(result.Ddl.Count(e => e.StartsWith("CREATE TABLE")), Is.EqualTo(1));
            Assert.That(Check(Load(), "Customer", "Tags"), Is.Empty);

            //もう一度: 何も作らず、結び付き済みの TagField も触らない
            var again = TagSetupService.Run(Load(), ProjectDir, Options("Customer"), DataSourceType.SQLite);
            Assert.That(again.CreatedModules, Is.Empty);
            Assert.That(again.Ddl, Is.Empty);
        }

        [Test]
        public void 結び付きなしの同名TagFieldは結び付けフォルダ分けのモジュールはその場で保存する()
        {
            CreateFixture();
            //列でタグを持っていた頃の TagField (今のデザインでは結び付きなし) が、Modules のサブフォルダにある
            var contact = new ModuleDesign { Name = "Contact", DataSourceName = "Main", DbTable = "contacts" };
            contact.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            contact.Fields.Add(new TagFieldDesign { Name = "タグ", DisplayName = "タグ", Placeholder = "タグを入力" });
            Directory.CreateDirectory(Path.Combine(ProjectDir, "Modules", "SFA"));
            WriteFile(Path.Combine("Modules", "SFA", "Contact.mod.json"), JsonConverterEx.SerializeObject(contact));

            var options = Options("Contact");
            options.FieldName = "タグ";
            options.LinkModuleName = "ContactTag";
            options.LinkTableName = "contact_tags";
            TagSetupService.Run(Load(), ProjectDir, options, DataSourceType.SQLite);

            Assert.That(File.Exists(Path.Combine(ProjectDir, "Modules", "Contact.mod.json")), Is.False);
            var d = Load();
            var field = d.Modules.Find("Contact")!.Fields.OfType<TagFieldDesign>().Single();
            Assert.That((field.Name, field.Placeholder, field.SearchCondition.ModuleName), Is.EqualTo(("タグ", "タグを入力", "ContactTag")));
            Assert.That(d.Modules.Find("ContactTag")!.DbTable, Is.EqualTo("contact_tags"));
            Assert.That(Check(d, "Contact", "タグ"), Is.Empty);
        }

        [Test]
        public void タグ付けの列はIdの型でNOTNULL()
        {
            foreach (var (type, idType) in new[] { (DataSourceType.SQLServer, "BIGINT"), (DataSourceType.PostgreSQL, "BIGINT"), (DataSourceType.MySQL, "BIGINT"), (DataSourceType.Oracle, "NUMBER"), (DataSourceType.SQLite, "INTEGER") })
            {
                SetUpProjectDir();
                CreateFixture();
                var ddl = TagSetupService.Run(Load(), ProjectDir, Options(), type).Ddl.Select(e => e.Trim()).ToList();
                Assert.That(ddl, Does.Contain($"request_id {idType} NOT NULL,"), type.ToString());
                Assert.That(ddl, Does.Contain($"tag_id {idType} NOT NULL"), type.ToString());
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
        public async Task 生成したデザインとDDLでタグを保存できる()
        {
            CreateFixture();
            var ddl = TagSetupService.Run(Load(), ProjectDir, Options(), DataSourceType.SQLite).Ddl;
            var design = Load();
            //固定のユーザーモジュール (Example の AppUser) は別のデータソース。このテストはユーザーを使わない
            design.AppSettings.CurrentUserModuleDesignName = string.Empty;
            //セットアップの次の手順 (ユーザーが行う): 詳細レイアウトに TagField を置く
            design.Modules.Find("Request")!.DetailLayouts[""] = new DetailLayoutDesign { DataOnlyFields = { "Title", "Tags" } };

            var dbFile = Path.Combine(Path.GetTempPath(), $"tag_setup_{Guid.NewGuid():N}.db");
            var db = new DbAccessor([new DataSource { Name = "Main", DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={dbFile}" }]);
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
                var names = await db.QueryAsync("Main", "SELECT name FROM tags ORDER BY id", new());
                Assert.That(names.Select(e => e["name"]), Is.EqualTo(new[] { "展示会", "DXPO" }));

                //同じ名前 (大文字小文字違い) をもう 1 行作ろうとしても一意インデックスが止める
                Assert.ThrowsAsync<SqliteException>(async () => await db.ExecuteAsync("Main", "INSERT INTO tags (name) VALUES ('dxpo')", new()));
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
