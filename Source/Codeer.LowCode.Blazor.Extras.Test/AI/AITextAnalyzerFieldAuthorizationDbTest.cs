using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Extras.Server.FileManagement;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// AITextAnalyzerField の解析 API (AITextAnalyzeService.AnalyzeFileAsync / AnalyzeTextAsync) のサーバー側ブロックを実 DB (SQLite) で検証する。
    /// クライアントを信用しない前提で「/api/ai_text_analyze に直接リクエストが来た」状況を再現する。
    /// AI サービスには繋がない: 検査は AI 呼び出しの前にあるので、拒否は LowCodeException で AI に届かないことを確かめ、
    /// 許可側は検査メソッド (CheckAsync) がデザイン (Remarks) を返すことで確かめる。
    /// ユーザー: "1"=一般 (Rank 1) / "2"=管理者 (Rank 10) / "3"=停止 (IsActive=false)
    /// モジュール Item: UserReadCondition = Rank &gt;= 5。AITextAnalyzerField "Analyze" (Remarks あり) のほかに、
    ///   PermissionField で Rank &gt;= 20 にしか見せない "SecretAnalyze" を持つ。
    /// モジュール Memo: DB に繋がっていないモジュール (誰でも開ける)。
    /// </summary>
    public class AITextAnalyzerFieldAuthorizationDbTest : IAuthenticationContext
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

            _dbFile = Path.Combine(Path.GetTempPath(), $"aitextanalyzer_auth_test_{Guid.NewGuid():N}.db");
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
            item.Fields.Add(new AITextAnalyzerFieldDesign { Name = "Analyze", Remarks = "請求書として読む" });
            item.Fields.Add(new AITextAnalyzerFieldDesign { Name = "SecretAnalyze" });
            var permission = new PermissionFieldDesign { Name = "P", TargetFields = { "SecretAnalyze" } };
            permission.ReadCondition.Condition = Match("CurrentUser.Rank.Value", MatchComparison.GreaterThanOrEqual, MultiTypeValue.Create(20));
            item.Fields.Add(permission);
            d.AddModule(item);

            //DB 無しモジュール (画面だけのフォーム)。開ける人は誰でも
            var memo = new ModuleDesign { Name = "Memo" };
            memo.Fields.Add(new TextFieldDesign { Name = "Title" });
            memo.Fields.Add(new AITextAnalyzerFieldDesign { Name = "Analyze" });
            d.AddModule(memo);

            return d;
        }

        //AI サービスには繋がない。検査で止まる経路は「呼ばれないこと」を、通る経路は偽の IChatClient の返事が取り込まれることを確かめる
        class FakeChatClient(string reply) : IChatClient
        {
            public List<IEnumerable<ChatMessage>> Calls { get; } = new();

            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                Calls.Add(messages.ToList());
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
            }

            public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }

        static (AITextAnalyzeService Service, FakeChatClient Chat) CreateService(string reply = "{}")
        {
            var chat = new FakeChatClient(reply);
            return (new AITextAnalyzeService(new AISettings(), () => chat), chat);
        }

        #endregion

        [Test]
        public async Task フィールドが見えるユーザーは検査を通りデザインのRemarksが使われる()
        {
            var design = await AITextAnalyzeService.CheckAsync(CreateIO("2"), "Item", "Analyze");
            Assert.That(design.Remarks, Is.EqualTo("請求書として読む"));

            //DB 無しモジュールは一般ユーザーでも
            var memo = await AITextAnalyzeService.CheckAsync(CreateIO("1"), "Memo", "Analyze");
            Assert.That(memo.Name, Is.EqualTo("Analyze"));
        }

        [Test]
        public async Task 見えるユーザーの解析はIChatClient経由でモデルを呼びデザインのRemarksが指示に入る()
        {
            var (service, chat) = CreateService("{\"Title\":\"請求書A\"}");
            var data = await service.AnalyzeTextAsync(CreateIO("2"), _designData.Modules, "Item", "Analyze", "請求書A 合計 1000 円");

            Assert.That(data.Name, Is.EqualTo("Item"));
            Assert.That((data.Fields["Title"] as TextFieldData)?.Value, Is.EqualTo("請求書A"));
            //システム指示にデザインの補足指示 (Remarks) が入り、ユーザー入力が最後に渡る
            var messages = chat.Calls.Single().ToList();
            Assert.That(messages[0].Role, Is.EqualTo(ChatRole.System));
            Assert.That(messages[0].Text, Does.Contain("請求書として読む"));
            Assert.That(messages.Last().Text, Is.EqualTo("請求書A 合計 1000 円"));
        }

        [Test]
        public void モジュールを開けないユーザー_フィールド読取権限のないユーザー_停止ユーザー_フィールド違い_存在しないモジュールはAIに届く前に拒否()
        {
            var (service, chat) = CreateService();
            var modules = _designData.Modules;
            //UserReadCondition 不成立
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("1"), modules, "Item", "Analyze", "text"));
            //PermissionField で隠された AITextAnalyzerField (モジュールは開けるがフィールドが見えない)
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("2"), modules, "Item", "SecretAnalyze", "text"));
            //停止ユーザー (AppAccessConditions 不成立)。DB 無しモジュールでも入口で弾かれる
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("3"), modules, "Item", "Analyze", "text"));
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("3"), modules, "Memo", "Analyze", "text"));
            //AITextAnalyzerField ではないフィールド・存在しないフィールド・存在しないモジュール・未指定
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("2"), modules, "Item", "Title", "text"));
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("2"), modules, "Item", "Nope", "text"));
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("2"), modules, "Nope", "Analyze", "text"));
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeTextAsync(CreateIO("2"), modules, "", "", "text"));

            //ファイル解析も同じ入口 (ファイルの読み取りより前に止まる)
            using var stream = new MemoryStream(new byte[] { 1, 2, 3 });
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeFileAsync(CreateIO("1"), modules, "Item", "Analyze", "a.pdf", stream));
            Assert.ThrowsAsync<LowCodeException>(async () => await service.AnalyzeFileAsync(CreateIO("2"), modules, "Item", "SecretAnalyze", "a.pdf", stream));

            Assert.That(chat.Calls, Is.Empty, "拒否された解析はモデルを呼ばない");
        }
    }
}
