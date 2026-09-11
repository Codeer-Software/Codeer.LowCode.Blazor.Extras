using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.Mail;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.Mail
{
    /// <summary>
    /// BulkMailField の一斉送信・プレビューのサーバー側ブロックを実 DB (SQLite) で検証する。
    /// クライアントを信用しない前提で「/api/mail/bulk_search に直接リクエストが来た」状況を再現する。
    /// ユーザー: "1"=一般 (Rank 1) / "2"=管理者 (Rank 10) / "3"=停止 (IsActive=false)
    /// モジュール Campaign (配信): UserReadCondition = Rank &gt;= 5。BulkMailField "Bulk" のほかに、
    ///   PermissionField で Rank &gt;= 20 にしか見せない BulkMailField "SecretBulk" を持つ。
    /// モジュール Member (宛先): 宛先契約 (Email / OptOut)。DataReadCondition = Name != 'hidden' (宛先側の行権限は従来どおり GetListAsync で効く)。
    /// </summary>
    public class BulkMailFieldSendAuthorizationDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "2";
        DesignData _designData = null!;

        public async Task<string> GetCurrentUserIdAsync()
        {
            await Task.CompletedTask;
            return _currentUserId;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();

            _dbFile = Path.Combine(Path.GetTempPath(), $"bulkmailfield_auth_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Rank INTEGER, IsActive INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 1, 1), ('2', 10, 1), ('3', 10, 0)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Campaigns (Id INTEGER PRIMARY KEY, Title TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Campaigns VALUES (1, 'summer')", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Members (Id INTEGER PRIMARY KEY, Name TEXT, Email TEXT, OptOut INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Members VALUES (1, 'a', 'a@example.com', 0), (2, 'b', 'b@example.com', 1), (3, 'hidden', 'c@example.com', 0)", new());

            _designData = CreateDesignData();
            _currentUserId = "2";
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(_dbFile); } catch { }
        }

        #region ハーネス

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す
        ModuleDataIO CreateIO(string userId)
        {
            _currentUserId = userId;
            return new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>()));
        }

        static FieldValueMatchCondition Match(string variable, MatchComparison comparison, MultiTypeValue value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = comparison,
            Value = value,
        };

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            //停止ユーザーはアプリに入れない
            d.AppSettings.AppAccessConditions.ModuleName = "AppUser";
            d.AppSettings.AppAccessConditions.Condition = Match("IsActive.Value", MatchComparison.Equal, MultiTypeValue.Create(true));

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new NumberFieldDesign { Name = "Rank", DbColumn = "Rank" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "IsActive" });
            d.AddModule(user);

            //宛先 (行) モジュール。誰でも開けるが 'hidden' の行は読めない
            var member = new ModuleDesign { Name = "Member", DataSourceName = Ds, DbTable = "Members" };
            member.DataReadCondition.ModuleName = "Member";
            member.DataReadCondition.Condition = Match("Name.Value", MatchComparison.NotEqual, MultiTypeValue.Create("hidden"));
            member.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            member.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name" });
            member.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "Email" });
            member.Fields.Add(new BooleanFieldDesign { Name = "OptOut", DbColumn = "OptOut" });
            member.Fields.Add(new BulkMailRecipientContractFieldDesign { Name = "Recipient", Email = "Email.Value", OptOut = "OptOut.Value", DisplayName = "Name.Value" });
            d.AddModule(member);

            //配信モジュール。Rank >= 5 だけ開ける
            var campaign = new ModuleDesign { Name = "Campaign", DataSourceName = Ds, DbTable = "Campaigns" };
            campaign.UserReadCondition.ModuleName = "AppUser";
            campaign.UserReadCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            campaign.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            campaign.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            campaign.Fields.Add(new ListFieldDesign { Name = "Members", SearchCondition = new() { ModuleName = "Member" } });
            campaign.Fields.Add(new BulkMailFieldDesign { Name = "Bulk", MailInfraName = "Main", RecipientListFieldName = "Members", Subject = "s", Body = "b" });
            campaign.Fields.Add(new BulkMailFieldDesign { Name = "SecretBulk", MailInfraName = "Main", RecipientListFieldName = "Members", Subject = "s", Body = "b" });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "SecretBulk" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            campaign.Fields.Add(permission);
            d.AddModule(campaign);

            return d;
        }

        class FakeMailSender : IMailSender
        {
            public List<(MailBulkTemplate Template, List<MailBulkRecipient> Recipients)> SentBulk { get; } = new();
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message) => Task.FromResult(MailSendResult.Success(1));

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
            {
                SentBulk.Add((template, recipients));
                return Task.FromResult(MailSendResult.Success(recipients.Count));
            }
        }

        //対応表は "Main" だけ知っている (クライアントが別の呼び名を送っても使われないことの確認用)
        static (MailDispatcher Dispatcher, FakeMailSender Fake) CreateDispatcher()
        {
            var fake = new FakeMailSender();
            return (new MailDispatcher(new MailConfig(), name => name == "Main" ? fake : null), fake);
        }

        static MailBulkSearchRequest CreateRequest(string fieldName = "Bulk", string infraName = "Evil", string sourceModule = "Campaign") => new()
        {
            MailInfraName = infraName,
            SourceModule = sourceModule,
            SourceId = "1",
            FieldName = fieldName,
            Subject = "{Name} 様",
            Body = "body",
            Condition = new SearchCondition { ModuleName = "Member" },
        };

        MailBulkSearch CreateSearch(MailDispatcher dispatcher, string userId)
            => new(dispatcher, CreateIO(userId), _designData);

        #endregion

        [Test]
        public async Task フィールドが見えるユーザーの送信はデザインの呼び名で送り宛先は読める行だけ()
        {
            var (dispatcher, fake) = CreateDispatcher();
            var result = await CreateSearch(dispatcher, "2").SendAsync(CreateRequest(infraName: "Evil"));
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(e => e.Error)));

            //呼び名 "Evil" は対応表に無い = デザインの "Main" で送られた証拠
            var (template, recipients) = fake.SentBulk.Single();
            Assert.That(template.Subject, Is.EqualTo("{Name} 様"));
            //'hidden' は DataReadCondition で読めず、b は配信停止
            Assert.That(recipients.Select(e => e.To), Is.EqualTo(new[] { "a@example.com" }));
        }

        [Test]
        public void モジュールを開けないユーザー_フィールド読取権限のないユーザー_停止ユーザー_フィールド違い_存在しないモジュールは送れない()
        {
            var (dispatcher, fake) = CreateDispatcher();
            //UserReadCondition 不成立 (宛先 Member 自体は誰でも読めるが、配信モジュールが開けない)
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "1").SendAsync(CreateRequest()));
            //PermissionField で隠された BulkMailField (モジュールは開けるがフィールドが見えない)
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "2").SendAsync(CreateRequest(fieldName: "SecretBulk")));
            //停止ユーザー (AppAccessConditions 不成立)
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "3").SendAsync(CreateRequest()));
            //BulkMailField ではないフィールド・存在しないフィールド・存在しないモジュール
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "2").SendAsync(CreateRequest(fieldName: "Title")));
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "2").SendAsync(CreateRequest(fieldName: "Nope")));
            Assert.ThrowsAsync<LowCodeException>(async () => await CreateSearch(dispatcher, "2").SendAsync(CreateRequest(sourceModule: "Nope")));
            Assert.That(fake.SentBulk, Is.Empty);
        }

        [Test]
        public async Task 一斉プレビューは同じ検査を通りデザインの呼び名で宛先一覧を載せる()
        {
            var (dispatcher, _) = CreateDispatcher();
            var request = CreateRequest(infraName: "Evil");

            var doc = await new MailPreviewBuilder(dispatcher, CreateIO("2"), _designData).BuildBulkAsync(request);
            Assert.That(doc.Kind, Is.EqualTo("bulk"));
            Assert.That(doc.MailInfraName, Is.EqualTo("Main"));       //クライアントの "Evil" ではなくデザインの呼び名
            Assert.That(doc.MaxBulkCount, Is.EqualTo(10000));
            //読める行だけ。配信停止は除外理由付きで残る (並び順は問わない)
            Assert.That(doc.Items.Select(e => (e.To, e.Excluded)).OrderBy(e => e.To), Is.EqualTo(new[] { ("a@example.com", (string?)null), ("b@example.com", "OptOut") }));
            Assert.That(doc.Items.Single(e => e.To == "a@example.com").Subject, Is.EqualTo("a 様"));
            Assert.That(doc.SendCount, Is.EqualTo(1));

            //モジュールを開けないユーザー・隠されたフィールドはプレビューも拒否
            Assert.ThrowsAsync<LowCodeException>(async () => await new MailPreviewBuilder(dispatcher, CreateIO("1"), _designData).BuildBulkAsync(request));
            Assert.ThrowsAsync<LowCodeException>(async () => await new MailPreviewBuilder(dispatcher, CreateIO("2"), _designData).BuildBulkAsync(CreateRequest(fieldName: "SecretBulk")));
        }
    }
}
