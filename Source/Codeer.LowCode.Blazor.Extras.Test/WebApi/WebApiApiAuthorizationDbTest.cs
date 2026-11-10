using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Api;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Extras.Server.WebApi;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Test.WebApi
{
    /// <summary>
    /// 共通の API の WebApi.Execute (WebApiApi) を実 DB (SQLite) の権限で確かめる。クライアントを信用しない前提で「/api/extras に直接要求が来た」状況を再現する。
    /// ユーザー: "1"=一般 (Rank 1) / "2"=管理者 (Rank 10)
    /// モジュール Item: UserWriteCondition = Rank &gt;= 5。WebApiField "Api" (GET users/{id}) / "Post" (POST) と、PermissionField で Rank &gt;= 20 にしか書かせない "SecretApi"。
    /// </summary>
    public class WebApiApiAuthorizationDbTest : IAuthenticationContext
    {
        const string Ds = "Main";

        DbAccessor _db = null!;
        string _dbFile = null!;
        string _currentUserId = "2";
        DesignData _designData = null!;
        RecordingHandler _handler = null!;
        WebApiApi _api = null!;

        public async Task<string> GetCurrentUserIdAsync()
        {
            await Task.CompletedTask;
            return _currentUserId;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"webapi_auth_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor(new[]
            {
                new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }
            });
            await _db.ExecuteAsync(Ds, "CREATE TABLE AppUsers (Id TEXT PRIMARY KEY, Rank INTEGER)", new());
            await _db.ExecuteAsync(Ds, "INSERT INTO AppUsers VALUES ('1', 1), ('2', 10)", new());
            await _db.ExecuteAsync(Ds, "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Title TEXT)", new());
            _designData = CreateDesignData();
            _handler = new RecordingHandler();
            _api = new WebApiApi(new WebApiClient(WebApiTestHelpers.OneConnection(), [], _handler));
        }

        [TearDown]
        public void TearDown()
        {
            _db.Dispose();
            _handler.Dispose();
            SqliteTestDb.Delete(_dbFile);
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

            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "AppUsers" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            user.Fields.Add(new NumberFieldDesign { Name = "Rank", DbColumn = "Rank" });
            d.AddModule(user);

            var item = new ModuleDesign { Name = "Item", DataSourceName = Ds, DbTable = "Items", CanCreate = true, CanUpdate = true };
            item.UserWriteCondition.ModuleName = "AppUser";
            item.UserWriteCondition.Condition = Match("Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(5));
            item.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "Title" });
            item.Fields.Add(new WebApiFieldDesign { Name = "Api", ConnectionName = "Api", Method = WebApiMethod.Get, Path = "users/{id}" });
            item.Fields.Add(new WebApiFieldDesign { Name = "Post", ConnectionName = "Api", Method = WebApiMethod.Post, Path = "orders" });
            item.Fields.Add(new WebApiFieldDesign { Name = "SecretApi", ConnectionName = "Api", Path = "secret" });
            item.Fields.Add(new WebApiFieldDesign { Name = "UpperPost", ConnectionName = "Api", Method = WebApiMethod.Post, Path = "orders/{id}", UseUpperCamelCaseNames = true });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "SecretApi" } };
            permission.WriteCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            item.Fields.Add(permission);
            d.AddModule(item);
            return d;
        }

        //AuthorizationChecker が CurrentUser をキャッシュするため、ユーザーごとに作り直す
        ExtrasApiContext CreateContext(string userId, AuditContext? audit = null)
        {
            _currentUserId = userId;
            return new ExtrasApiContext
            {
                ModuleDataIO = new ModuleDataIO(_designData, this, _db, new TemporaryFileManager(_db, [], new List<IFileStorage>())),
                DesignData = _designData,
                DbAccess = _db,
                UserId = userId,
                Audit = audit,
            };
        }

        static string Payload(string module, string field, string arguments)
            => JsonConverterEx.SerializeObject(new WebApiExecuteRequest { ModuleName = module, FieldName = field, Arguments = arguments }, indented: false);

        [Test]
        public async Task 書けるユーザーはデザインの接続先とメソッドとパスで呼べる()
        {
            var ret = (WebApiExecuteResponse?)await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Api", """{"id":3}"""), CreateContext("2"));
            Assert.That(ret!.StatusCode, Is.EqualTo(200));
            var sent = _handler.Requests.Single();
            Assert.That(sent.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/3"));
        }

        [Test]
        public async Task サーバーは引数のキーをデザインの指定にかかわらず変えない()
        {
            await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Post", """{"OrderNo":"A1","lines":[{"ItemCode":"X"}]}"""), CreateContext("2"));
            Assert.That(_handler.Requests[0].Body, Is.EqualTo("""{"OrderNo":"A1","lines":[{"ItemCode":"X"}]}"""));

            //UseUpperCamelCaseNames はクライアントの Execute(args) のための指定 (差し込みの {id} は id と合う)
            await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "UpperPost", """{"id":3,"orderNo":"A1"}"""), CreateContext("2"));
            Assert.That(_handler.Requests[1].Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/orders/3"));
            Assert.That(_handler.Requests[1].Body, Is.EqualTo("""{"id":3,"orderNo":"A1"}"""));
        }

        [Test]
        public void 書けないユーザーは相手に届く前に拒否()
        {
            //UserWriteCondition 不成立
            Assert.ThrowsAsync<LowCodeAccessDeniedException>(async () => await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Api", "{}"), CreateContext("1")));
            //PermissionField の書き込み条件 (管理者でも Rank 20 未満)
            Assert.ThrowsAsync<LowCodeAccessDeniedException>(async () => await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "SecretApi", "{}"), CreateContext("2")));
            Assert.That(_handler.Requests, Is.Empty);
        }

        [Test]
        public void WebApiFieldでないフィールドや無いモジュールは例外()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Title", "{}"), CreateContext("2")));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Nope", "{}"), CreateContext("2")));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await _api.ExecuteAsync(WebApiNames.Execute, Payload("Nope", "Api", "{}"), CreateContext("2")));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await _api.ExecuteAsync("Other", Payload("Item", "Api", "{}"), CreateContext("2")));
            Assert.That(_handler.Requests, Is.Empty);
        }

        [Test]
        public async Task 接続先とメソッドとパスはクライアントから差し替えられない()
        {
            //要求に接続先・メソッド・パス・URL を混ぜても読まない
            var payload = """
                {"ModuleName":"Item","FieldName":"Api","Arguments":"{\"id\":3}",
                 "ConnectionName":"Evil","Method":"Delete","Path":"admin","BaseUrl":"https://evil.example.com/","Url":"https://evil.example.com/"}
                """;
            var ret = (WebApiExecuteResponse?)await _api.ExecuteAsync(WebApiNames.Execute, payload, CreateContext("2"));
            Assert.That(ret!.StatusCode, Is.EqualTo(200));
            var sent = _handler.Requests.Single();
            Assert.That(sent.Method, Is.EqualTo(HttpMethod.Get));
            Assert.That(sent.Uri.AbsoluteUri, Is.EqualTo("https://api.example.com/v1/users/3"));
        }

        [Test]
        public async Task 監査の区分はGetが読み取りでそれ以外は書き込み_相手の失敗は失敗として残す()
        {
            Assert.That(_api.GetAuditCategory(WebApiNames.Execute), Is.EqualTo(AuditCategory.DataRead));

            var audit = new AuditContext();
            await audit.StartAsync(_api.GetAuditCategory(WebApiNames.Execute), "WebApi.Execute");
            await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Api", """{"id":3}"""), CreateContext("2", audit));
            Assert.That(audit.Event.Category, Is.EqualTo(AuditCategory.DataRead));
            Assert.That(audit.Event.Targets.Single().Operation, Is.EqualTo("WebApi:Get:Api"));
            Assert.That(audit.Event.Result, Is.Not.EqualTo(AuditResult.Failure));

            _handler.Responder = (_, _) => Task.FromResult(RecordingHandler.Json(System.Net.HttpStatusCode.BadRequest, "{}"));
            audit = new AuditContext();
            await audit.StartAsync(_api.GetAuditCategory(WebApiNames.Execute), "WebApi.Execute");
            var ret = (WebApiExecuteResponse?)await _api.ExecuteAsync(WebApiNames.Execute, Payload("Item", "Post", "{}"), CreateContext("2", audit));
            Assert.That(ret!.StatusCode, Is.EqualTo(400));
            Assert.That(audit.Event.Category, Is.EqualTo(AuditCategory.DataWrite));
            Assert.That(audit.Event.Action, Is.EqualTo("WebApi.Execute"));
            Assert.That(audit.Event.Result, Is.EqualTo(AuditResult.Failure));
            Assert.That(audit.Event.Detail, Is.EqualTo("HTTP 400"));
        }
    }
}
