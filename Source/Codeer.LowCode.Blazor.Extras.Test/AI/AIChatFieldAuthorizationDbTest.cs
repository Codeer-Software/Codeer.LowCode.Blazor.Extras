using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// AIChatField の送信 (AIChatJobStore.StartAsync) のサーバー側ブロックを実 DB (SQLite) で検証する。
    /// クライアントを信用しない前提で「/api/ai_chat に直接リクエストが来た」状況を再現する。
    /// ユーザー: "1"=一般 (Rank 1) / "2"=管理者 (Rank 10) / "3"=停止 (IsActive=false)
    /// モジュール Item: UserReadCondition = Rank &gt;= 5。AIChatField "Chat" (Agent=Main / DocumentFolder=AIChat/Sales) のほかに、
    ///   PermissionField で Rank &gt;= 20 にしか見せない AIChatField "SecretChat" を持つ。
    /// モジュール Memo: DB に繋がっていないモジュール (誰でも開ける)。AIChatField "Chat" は Agent 空 (既定)。
    /// </summary>
    public class AIChatFieldAuthorizationDbTest : IAuthenticationContext
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

            _dbFile = Path.Combine(Path.GetTempPath(), $"aichatfield_auth_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });

            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Rank INTEGER, IsActive INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 1, 1), ('2', 10, 1), ('3', 10, 0)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Title TEXT)", new());

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

            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items" };
            item.UserReadCondition.ModuleName = "AppUser";
            item.UserReadCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            item.Fields.Add(new AIChatFieldDesign { Name = "Chat", Agent = "Main", DocumentFolder = "AIChat/Sales" });
            item.Fields.Add(new AIChatFieldDesign { Name = "SecretChat", Agent = "Main" });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "SecretChat" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            item.Fields.Add(permission);
            d.AddModule(item);

            //DB 無しモジュール (画面だけのフォーム)。開ける人は誰でも。Agent は既定
            var memo = new ModuleDesign { Name = "Memo" };
            memo.Fields.Add(new TextFieldDesign { Name = "Title" });
            memo.Fields.Add(new AIChatFieldDesign { Name = "Chat" });
            d.AddModule(memo);

            return d;
        }

        //対応表は "Main" と既定 (空) だけ知っている (クライアントが別の名前を送っても使われないことの確認用)
        static (AIChatJobStore Store, FakeAIChatAgent Fake) CreateStore()
        {
            var fake = new FakeAIChatAgent();
            return (new AIChatJobStore(name => name is "Main" or "" ? fake : null), fake);
        }

        static AIChatSendRequest CreateRequest(string moduleName = "Item", string fieldName = "Chat") => new()
        {
            ConversationId = "conv1",
            Message = "こんにちは",
            //クライアントの参考値。サーバーはデザインの値を使う
            Agent = "Evil",
            DocumentFolder = "../../secret",
            ModuleName = moduleName,
            FieldName = fieldName,
        };

        static async Task<AIChatStatusResponse> WaitDoneAsync(AIChatJobStore store, string owner, string id, int timeoutMs = 10000)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                var s = store.GetStatus(owner, id);
                Assert.That(s, Is.Not.Null);
                if (!s!.IsRunning) return s;
                await Task.Delay(20);
            }
            throw new TimeoutException();
        }

        #endregion

        [Test]
        public async Task フィールドが見えるユーザーの送信はデザインのAgentと文書フォルダで走りクライアントの値は使わない()
        {
            var (store, fake) = CreateStore();
            using var _ = store;
            var id = await store.StartAsync("owner2", CreateRequest(), CreateIO("2"));
            var status = await WaitDoneAsync(store, "owner2", id);
            Assert.That(status.Status, Is.EqualTo(AIChatJobStatus.Done), status.Error);

            var request = fake.Requests.Single();
            Assert.That(request.AgentName, Is.EqualTo("Main"));             //クライアントの "Evil" ではなくデザインの値
            Assert.That(request.DocumentFolder, Is.EqualTo("AIChat/Sales")); //同上
            Assert.That(request.UserName, Is.EqualTo("owner2"));
            Assert.That(request.ConversationId, Is.EqualTo("conv1"));
            Assert.That(request.Message, Is.EqualTo("こんにちは"));
        }

        [Test]
        public async Task DB無しモジュールに置いたAIChatFieldは一般ユーザーでも既定Agentで送れる()
        {
            var (store, fake) = CreateStore();
            using var _ = store;
            var id = await store.StartAsync("owner1", CreateRequest(moduleName: "Memo"), CreateIO("1"));
            var status = await WaitDoneAsync(store, "owner1", id);
            Assert.That(status.Status, Is.EqualTo(AIChatJobStatus.Done), status.Error);
            Assert.That(fake.Requests.Single().AgentName, Is.Empty);
            Assert.That(fake.Requests.Single().DocumentFolder, Is.Empty);
        }

        [Test]
        public void モジュールを開けないユーザー_フィールド読取権限のないユーザー_停止ユーザー_フィールド違い_存在しないモジュールは送れずジョブも作られない()
        {
            var (store, fake) = CreateStore();
            using var _ = store;
            //UserReadCondition 不成立
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner1", CreateRequest(), CreateIO("1")));
            //PermissionField で隠された AIChatField (モジュールは開けるがフィールドが見えない)
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner2", CreateRequest(fieldName: "SecretChat"), CreateIO("2")));
            //停止ユーザー (AppAccessConditions 不成立)。DB 無しモジュールでも入口で弾かれる
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner3", CreateRequest(), CreateIO("3")));
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner3", CreateRequest(moduleName: "Memo"), CreateIO("3")));
            //AIChatField ではないフィールド・存在しないフィールド・存在しないモジュール・未指定
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner2", CreateRequest(fieldName: "Title"), CreateIO("2")));
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner2", CreateRequest(fieldName: "Nope"), CreateIO("2")));
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner2", CreateRequest(moduleName: "Nope"), CreateIO("2")));
            Assert.ThrowsAsync<LowCodeException>(async () => await store.StartAsync("owner2", CreateRequest(moduleName: "", fieldName: ""), CreateIO("2")));

            Assert.That(store.Count, Is.EqualTo(0), "拒否された送信はジョブにならない");
            Assert.That(fake.Requests, Is.Empty);
        }
    }
}
