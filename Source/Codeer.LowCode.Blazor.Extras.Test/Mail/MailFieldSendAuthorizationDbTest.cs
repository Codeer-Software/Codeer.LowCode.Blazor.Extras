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
    /// MailField の送信・プレビューのサーバー側ブロックを実 DB (SQLite) で検証する。
    /// クライアントを信用しない前提で「/api/mail に直接リクエストが来た」状況を再現する。
    /// ユーザー: "1"=一般 (Rank 1) / "2"=管理者 (Rank 10) / "3"=停止 (IsActive=false)
    /// モジュール Item: UserReadCondition = Rank &gt;= 5、DataReadCondition = Title == 'visible' (行 1 だけ読める。ただし送信では行を読まないので効かないことの確認用)。
    ///   MailField "Mail" のほかに、PermissionField で Rank &gt;= 20 にしか見せない MailField "SecretMail" を持つ。
    /// モジュール Memo: DB に繋がっていない (DataSourceName 空) モジュール。
    /// </summary>
    public class MailFieldSendAuthorizationDbTest : IAuthenticationContext
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

            _dbFile = Path.Combine(Path.GetTempPath(), $"mailfield_auth_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Rank INTEGER, IsActive INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 1, 1), ('2', 10, 1), ('3', 10, 0)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Title TEXT)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO Items VALUES (1, 'visible'), (2, 'hidden')", new());

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

        static FieldValueMatchCondition Eq(string variable, MultiTypeValue value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = MatchComparison.Equal,
            Value = value,
        };

        DesignData CreateDesignData()
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            //停止ユーザーはアプリに入れない
            d.AppSettings.AppAccessConditions.ModuleName = "AppUser";
            d.AppSettings.AppAccessConditions.Condition = Eq("IsActive.Value", MultiTypeValue.Create(true));

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new NumberFieldDesign { Name = "Rank", DbColumn = "Rank" });
            user.Fields.Add(new BooleanFieldDesign { Name = "IsActive", DbColumn = "IsActive" });
            d.AddModule(user);

            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items" };
            item.UserReadCondition.ModuleName = "AppUser";
            item.UserReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "Rank.Value",
                Comparison = MatchComparison.GreaterThanOrEqual,
                Value = MultiTypeValue.Create(5),
            };
            item.DataReadCondition.ModuleName = "Item";
            item.DataReadCondition.Condition = Eq("Title.Value", MultiTypeValue.Create("visible"));
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            item.Fields.Add(new MailFieldDesign { Name = "Mail", MailInfraName = "Main", To = "a@example.com", Subject = "s", Body = "b" });
            item.Fields.Add(new MailFieldDesign { Name = "SecretMail", MailInfraName = "Main", To = "a@example.com", Subject = "s", Body = "b" });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "SecretMail" } };
            permission.ReadCondition.Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = "CurrentUser.Rank.Value",
                Comparison = MatchComparison.GreaterThanOrEqual,
                Value = MultiTypeValue.Create(20),
            };
            item.Fields.Add(permission);
            d.AddModule(item);

            //DB 無しモジュール (画面だけのフォーム)。開ける人は誰でも
            var memo = new ModuleDesign { Name = "Memo" };
            memo.Fields.Add(new TextFieldDesign { Name = "Title" });
            memo.Fields.Add(new MailFieldDesign { Name = "Mail", MailInfraName = "Main", To = "a@example.com", Subject = "s", Body = "b" });
            d.AddModule(memo);

            return d;
        }

        class FakeMailSender : IMailSender
        {
            public List<MailMessage> Sent { get; } = new();
            public int MaxBulkCount => 10000;

            public Task<MailSendResult> SendAsync(MailMessage message)
            {
                Sent.Add(message);
                return Task.FromResult(MailSendResult.Success(1));
            }

            public Task<MailSendResult> SendBulkAsync(MailBulkTemplate template, List<MailBulkRecipient> recipients)
                => Task.FromResult(MailSendResult.Success(recipients.Count));
        }

        //対応表は "Main" だけ知っている (クライアントが別の呼び名を送っても使われないことの確認用)
        static (MailDispatcher Dispatcher, FakeMailSender Fake) CreateDispatcher()
        {
            var fake = new FakeMailSender();
            return (new MailDispatcher(new MailConfig(), name => name == "Main" ? fake : null), fake);
        }

        static MailSendRequest CreateRequest(string sourceId, string fieldName = "Mail", string infraName = "Evil", string sourceModule = "Item") => new()
        {
            MailInfraName = infraName,
            SourceModule = sourceModule,
            SourceId = sourceId,
            FieldName = fieldName,
            Message = new MailMessage { From = "spoof@example.com", FromDisplayName = "偽", To = { "to@example.com" }, Subject = "subject", Body = "body" },
        };

        #endregion

        [Test]
        public async Task フィールドが見えるユーザーの送信はデザインの呼び名で送りクライアントの呼び名と差出人は使わない()
        {
            var (dispatcher, fake) = CreateDispatcher();
            var result = await dispatcher.SendAsync(CreateRequest("1", infraName: "Evil"), CreateIO("2"));
            Assert.That(result.IsSuccess, Is.True, string.Join(";", result.Failures.Select(e => e.Error)));
            Assert.That(fake.Sent, Has.Count.EqualTo(1));
            Assert.That(fake.Sent[0].From, Is.Empty);
            Assert.That(fake.Sent[0].FromDisplayName, Is.Empty);
        }

        [Test]
        public async Task 行は読まないので読めない行_未保存_存在しない行_DB無しモジュールからも送れる()
        {
            var (dispatcher, fake) = CreateDispatcher();
            //DataReadCondition の外の行
            Assert.That((await dispatcher.SendAsync(CreateRequest("2"), CreateIO("2"))).IsSuccess, Is.True);
            //未保存 (Id 無し) と存在しない行
            Assert.That((await dispatcher.SendAsync(CreateRequest(string.Empty), CreateIO("2"))).IsSuccess, Is.True);
            Assert.That((await dispatcher.SendAsync(CreateRequest("999"), CreateIO("2"))).IsSuccess, Is.True);
            //DB 無しモジュールに置いた MailField (一般ユーザーでも開ける)
            Assert.That((await dispatcher.SendAsync(CreateRequest(string.Empty, sourceModule: "Memo"), CreateIO("1"))).IsSuccess, Is.True);
            Assert.That(fake.Sent, Has.Count.EqualTo(4));
        }

        [Test]
        public void モジュールを開けないユーザー_フィールド読取権限のないユーザー_停止ユーザー_フィールド違い_存在しないモジュールは送れない()
        {
            var (dispatcher, fake) = CreateDispatcher();
            //UserReadCondition 不成立
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1"), CreateIO("1")));
            //PermissionField で隠された MailField (モジュールは開けるがフィールドが見えない)
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1", fieldName: "SecretMail"), CreateIO("2")));
            //停止ユーザー (AppAccessConditions 不成立)。DB 無しモジュールでも入口で弾かれる
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1"), CreateIO("3")));
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("", sourceModule: "Memo"), CreateIO("3")));
            //MailField ではないフィールド・存在しないフィールド・存在しないモジュール
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1", fieldName: "Title"), CreateIO("2")));
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1", fieldName: "Nope"), CreateIO("2")));
            Assert.ThrowsAsync<LowCodeException>(async () => await dispatcher.SendAsync(CreateRequest("1", sourceModule: "Nope"), CreateIO("2")));
            Assert.That(fake.Sent, Is.Empty);
        }

        [Test]
        public async Task 単発プレビューは同じ検査を通りデザインの呼び名で文面と区間を載せる()
        {
            var (dispatcher, _) = CreateDispatcher();
            var request = new MailPreviewRequest
            {
                MailInfraName = "Evil",
                SourceModule = "Item",
                SourceId = "1",
                FieldName = "Mail",
                Title = "Item #1",
                SubjectTemplate = "注文 {No}",
                Message = new MailMessage
                {
                    To = { "a@example.com" }, Cc = { "c@example.com" }, Subject = "注文 100", Body = "本文",
                    Attachments = { new MailAttachment { FileName = "a.txt", ContentBase64 = Convert.ToBase64String("abc"u8.ToArray()) } },
                },
                SubjectSpans = { new MailTemplateSpan { Start = 3, Length = 3, Name = "No" } },
            };

            var doc = await new MailPreviewBuilder(dispatcher, CreateIO("2"), _designData).BuildSingleAsync(request);
            Assert.That(doc.Kind, Is.EqualTo("single"));
            Assert.That(doc.MailInfraName, Is.EqualTo("Main"));       //クライアントの "Evil" ではなくデザインの呼び名
            Assert.That(doc.From, Is.Empty);                           //差出人は送信インフラ設定のシステム送信者 (プレビューには載せない)
            Assert.That(doc.Items.Single().To, Is.EqualTo("a@example.com"));
            Assert.That(doc.Items.Single().Cc, Is.EqualTo(new[] { "c@example.com" }));
            Assert.That(doc.Items.Single().SubjectSpans.Single().Name, Is.EqualTo("No"));
            Assert.That(doc.Attachments, Is.EqualTo(new[] { "a.txt" }));
            Assert.That(doc.AttachmentFiles.Single().ContentBase64, Is.EqualTo(Convert.ToBase64String("abc"u8.ToArray())));
            Assert.That(doc.Warning, Is.Empty);
            //送信パッケージ (MailSender アプリが読む): 添付の内容と形式バージョンが JSON に載る
            Assert.That(doc.PackageVersion, Is.EqualTo(1));
            var json = doc.ToJson();
            Assert.That(json, Does.Contain("\"packageVersion\":1").And.Contain("\"attachmentFiles\"").And.Contain("\"contentBase64\""));

            //モジュールを開けないユーザーはプレビューも拒否。行は読まないので読めない行でも通る
            Assert.ThrowsAsync<LowCodeException>(async () => await new MailPreviewBuilder(dispatcher, CreateIO("1"), _designData).BuildSingleAsync(request));
            request.SourceId = "2";
            Assert.That((await new MailPreviewBuilder(dispatcher, CreateIO("2"), _designData).BuildSingleAsync(request)).Kind, Is.EqualTo("single"));
        }
    }
}
