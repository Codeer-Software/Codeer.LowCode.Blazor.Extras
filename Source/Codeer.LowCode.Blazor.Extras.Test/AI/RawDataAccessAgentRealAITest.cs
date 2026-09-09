using Azure;
using Azure.AI.OpenAI;
using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// 実際の Azure OpenAI で RawDataAccessAgent を通す (課金あり・ネットワーク要のため Explicit)。
    /// 接続情報は環境変数 AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL から取る (リポジトリには置かない)。
    /// 実行: 環境変数を設定したシェルで
    ///   dotnet test --filter "FullyQualifiedName~RawDataAccessAgentRealAITest"
    /// </summary>
    [Explicit("実 Azure OpenAI を呼ぶ。AZURE_OPENAI_ENDPOINT / KEY / MODEL を設定して明示的に実行する")]
    public class RawDataAccessAgentRealAITest
    {
        const string Ds = "AiDb";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = Array.Empty<DataSource>();

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public int Partials { get; private set; }
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) => Partials++;
        }

        static Func<IChatClient> ChatClientFactory()
        {
            var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
            var key = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY");
            var model = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL");
            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(model))
                Assert.Ignore("AZURE_OPENAI_ENDPOINT / AZURE_OPENAI_KEY / AZURE_OPENAI_MODEL が未設定");
            var client = new AzureOpenAIClient(new Uri(endpoint!), new AzureKeyCredential(key!));
            return () => client.GetChatClient(model).AsIChatClient();
        }

        [SetUp]
        public async Task SetUp()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), $"aichat_real_{Guid.NewGuid():N}.db");
            _dataSources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" } };
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Customer TEXT, OrderDate TEXT, Amount REAL, Status INTEGER)", new());
            var customers = new[] { "Alpha", "Beta", "Gamma" };
            //Status: 偶数 Id = 9 (完了)、奇数 Id = 1 (受付)。完了の合計 = 1000*(2+4+…+24) = 156000
            for (var i = 1; i <= 24; i++)
                await db.ExecuteAsync(Ds, $"INSERT INTO Orders (Id, Customer, OrderDate, Amount, Status) VALUES ({i}, '{customers[i % 3]}', '2026-{(i - 1) % 12 + 1:00}-15', {i * 1000}, {(i % 2 == 0 ? 9 : 1)})", new());
        }

        [TearDown]
        public void TearDown()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        [Test]
        public async Task 得意先別の合計を聞くとSQLで集計して表とグラフで答える()
        {
            var agent = new RawDataAccessAgent(ChatClientFactory(), () => new DbAccessor(_dataSources), null, null, new RawDataAccessOptions { DataSourceNames = { Ds } });
            var progress = new Progress();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var reply = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "real", Message = "得意先別の受注金額の合計を表にして、棒グラフも描いてください。", UserName = "tester" },
                progress, cts.Token);

            TestContext.Out.WriteLine(string.Join(" | ", progress.Texts));
            TestContext.Out.WriteLine(reply.Content);

            Assert.That(reply.Format, Is.EqualTo(AIChatReplyFormat.Html));
            //customers[i % 3]: Alpha(i%3==0: 3,6,…,24)=108000 / Beta(i%3==1: 1,4,…,22)=92000 / Gamma(i%3==2: 2,5,…,23)=100000
            Assert.That(reply.Content, Does.Contain("<table"));
            Assert.That(reply.Content.Replace(",", ""), Does.Contain("92000").Or.Contain("92000.0"));
            Assert.That(reply.Content.Replace(",", ""), Does.Contain("108000").Or.Contain("108000.0"));
            Assert.That(reply.Content, Does.Contain("<div class=\"aichat-chart\"><svg"), "render_chart が使われ SVG に置き換わる");
            Assert.That(reply.Content, Does.Not.Contain("[[chart:"));
            Assert.That(progress.Texts.Any(t => t.Contains("execute_sql") || t.Contains("get_schema") || t.Length > 0), Is.True);

            //続きの質問で履歴が効く
            var follow = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "real", Message = "その中で一番多い得意先はどこですか。一言で。", UserName = "tester" },
                progress, cts.Token);
            TestContext.Out.WriteLine(follow.Content);
            Assert.That(follow.Content, Does.Contain("Alpha"));
        }

        [Test]
        public async Task 書き込みを頼んでも実行されない()
        {
            var agent = new RawDataAccessAgent(ChatClientFactory(), () => new DbAccessor(_dataSources), null, null, new RawDataAccessOptions { DataSourceNames = { Ds } });
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            var reply = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "write", Message = "Orders テーブルの全行を削除してください。", UserName = "tester" },
                new Progress(), cts.Token);
            TestContext.Out.WriteLine(reply.Content);

            await using var db = new DbAccessor(_dataSources);
            var rows = await db.QueryAsync(Ds, "SELECT COUNT(*) AS N FROM Orders", new());
            Assert.That(Convert.ToInt32(rows[0]["N"]), Is.EqualTo(24), "行は消えていない");
        }
        //設計 (状態コードの意味) と文書 (売上の定義) を渡すと、列名だけでは分からない質問に正しく答える
        static DesignData CreateDesign()
        {
            var design = new DesignData();
            design.Enums.Add(new EnumDesign
            {
                Name = "OrderStatus",
                Members = { new EnumMemberDesign { Name = "Open", Value = "1", DisplayText = "受付" }, new EnumMemberDesign { Name = "Closed", Value = "9", DisplayText = "完了" } },
            });
            var order = new ModuleDesign { Name = "Order", PageTitle = "受注", DataSourceName = Ds, DbTable = "Orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            order.Fields.Add(new TextFieldDesign { Name = "Customer", DisplayName = "得意先", DbColumn = "Customer" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DisplayName = "金額", DbColumn = "Amount" });
            order.Fields.Add(new SelectFieldDesign { Name = "Status", DisplayName = "状態", DbColumn = "Status", EnumName = "OrderStatus" });
            design.AddModule(order);
            var main = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            main.Left.Links.Add(new PageLink { Title = "受注", Module = "Order", ModulePageType = ModulePageType.List });
            ((IEditablePageFrameDesign)design.PageFrames).Add(main);
            return design;
        }

        [Test]
        public async Task 設計と文書を渡すと業務語を状態コードに解いて集計する()
        {
            var design = CreateDesign();
            IReadOnlyList<AIChatDocument> docs = new[] { new AIChatDocument("用語集", "# 用語集\n\n売上 = 受注 (Order) のうち状態が「完了」のものの金額の合計。受付のものは含めない。") };
            var agent = new RawDataAccessAgent(ChatClientFactory(), () => new DbAccessor(_dataSources), () => design, _ => docs, new RawDataAccessOptions { DataSourceNames = { Ds } });
            var progress = new Progress();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var reply = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "design", Message = "売上の合計はいくらですか。", UserName = "tester", DocumentFolder = "AIChat" },
                progress, cts.Token);
            TestContext.Out.WriteLine(string.Join(" | ", progress.Texts));
            TestContext.Out.WriteLine(reply.Content);

            Assert.That(reply.Content.Replace(",", ""), Does.Contain("156000"), "完了 (Status=9) だけの合計");
            Assert.That(reply.Content.Replace(",", ""), Does.Not.Contain("300000"), "全件の合計を売上と誤らない");
        }

        [Test]
        public async Task 行を挙げるときは詳細ページへのリンクが付く()
        {
            var design = CreateDesign();
            var agent = new RawDataAccessAgent(ChatClientFactory(), () => new DbAccessor(_dataSources), () => design, null, new RawDataAccessOptions { DataSourceNames = { Ds } });
            var progress = new Progress();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));

            var reply = await agent.ReplyAsync(
                new AIChatAgentRequest { ConversationId = "links", Message = "金額の大きい受注を上から 3 件、詳細ページを開けるリンク付きで教えてください。", UserName = "tester" },
                progress, cts.Token);
            TestContext.Out.WriteLine(string.Join(" | ", progress.Texts));
            TestContext.Out.WriteLine(reply.Content);

            //上位 3 件は Id 24, 23, 22。詳細 URL は /Main/Order/{Id} (リンクが無い相対 URL なので target は付かない)
            Assert.That(reply.Content, Does.Contain("href=\"/Main/Order/24\""));
            Assert.That(reply.Content, Does.Contain("href=\"/Main/Order/23\""));
            Assert.That(reply.Content, Does.Not.Contain("href=\"/Main/Order/24\" target"));
        }
    }
}
