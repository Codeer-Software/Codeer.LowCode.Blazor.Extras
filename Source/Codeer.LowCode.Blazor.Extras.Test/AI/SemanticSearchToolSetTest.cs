using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>SemanticSearchToolSet (search_records): 索引済みの行を意味で探して似ている順に Id・URL・文章を返す。</summary>
    public class SemanticSearchToolSetTest
    {
        const string Ds = "Main";
        string _dbFile = string.Empty;
        DataSource[] _dataSources = Array.Empty<DataSource>();
        DesignData _design = null!;

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        static AIChatToolContext Context(Progress? progress = null)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, progress ?? new Progress(), CancellationToken.None, null);

        static DesignData CreateDesign()
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector" });
            d.AddModule(m);
            var other = new ModuleDesign { Name = "Plain", DataSourceName = Ds, DbTable = "plain" };
            other.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            d.AddModule(other);
            //Main フレームのサイドバーに Inquiry (セグメント inquiries) → URL は /Main/inquiries/{Id}
            var frame = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            frame.Left.Links.Add(new PageLink { Title = "問い合わせ", Module = "Inquiry", ModuleUrlSegment = "inquiries", ModulePageType = ModulePageType.List });
            ((IEditablePageFrameDesign)d.PageFrames).Add(frame);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            _dbFile = Path.Combine(Path.GetTempPath(), $"semantic_tool_test_{Guid.NewGuid():N}.db");
            _dataSources = new[] { new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" } };
            await using var db = new DbAccessor(_dataSources);
            await db.ExecuteAsync(Ds, "CREATE TABLE inquiries (id INTEGER PRIMARY KEY, subject TEXT, body TEXT, search_text TEXT, search_vector TEXT)", new());
            await db.ExecuteAsync(Ds, "CREATE TABLE plain (id INTEGER PRIMARY KEY)", new());
            var texts = new Dictionary<int, string>
            {
                [1] = "件名: 納期遅れの相談\n本文: 注文した商品がまだ届かない。納期を確認したい",
                [2] = "件名: 請求書の再発行\n本文: 宛名を変更して請求書を再発行してほしい",
                [3] = "件名: 納品が遅れている\n本文: 出荷が遅れて納期に間に合わない",
                [4] = "件名: パスワードを忘れた\n本文: ログインできない",
            };
            foreach (var (id, text) in texts)
                await db.ExecuteAsync(Ds, "INSERT INTO inquiries (id, subject, body, search_text, search_vector) VALUES (@p1, 's', 'b', @p2, @p3)",
                    new() { ["@p1"] = id, ["@p2"] = text, ["@p3"] = SemanticSearchVector.Encode(FakeEmbeddingGenerator.Embed(text)) });
            //索引がまだ無い行は検索に出ない
            await db.ExecuteAsync(Ds, "INSERT INTO inquiries (id, subject, body) VALUES (5, '納期', '未索引')", new());
            _design = CreateDesign();
        }

        [TearDown]
        public void TearDown()
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(_dbFile)) File.Delete(_dbFile);
        }

        SemanticSearchToolSet ToolSet(IList<string>? dataSourceNames = null)
            => new(() => _design, () => new DbAccessor(_dataSources), () => new FakeEmbeddingGenerator(), dataSourceNames ?? new List<string> { Ds });

        static async Task<JsonDocument> InvokeAsync(IEnumerable<AITool> tools, Dictionary<string, object?> args)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == "search_records");
            var result = await function.InvokeAsync(new AIFunctionArguments(args));
            var json = result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
            return JsonDocument.Parse(json);
        }

        [Test]
        public void 説明には意味検索できるモジュールと文章にしたフィールドが出る()
        {
            var text = ToolSet().GetInstructions(Context());
            Assert.That(text, Does.Contain("Inquiry [問い合わせ]").And.Contain("件名, 本文").And.Contain("search_records"));
            Assert.That(text, Does.Not.Contain("Plain"));
        }

        [Test]
        public async Task 似ている順にIdとURLと文章を返す()
        {
            var progress = new Progress();
            var tools = ToolSet().CreateTools(Context(progress)).ToList();
            Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "search_records" }));

            using var doc = await InvokeAsync(tools, new() { ["moduleName"] = "inquiry", ["query"] = "納期が遅れている注文", ["top"] = 2 });
            var root = doc.RootElement;
            Assert.That(root.GetProperty("module").GetString(), Is.EqualTo("Inquiry"));
            Assert.That(root.GetProperty("indexedCount").GetInt32(), Is.EqualTo(4));
            var results = root.GetProperty("results").EnumerateArray().ToList();
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.Select(r => r.GetProperty("id").GetString()), Is.EquivalentTo(new[] { "1", "3" }), "納期の 2 件が上位");
            Assert.That(results[0].GetProperty("score").GetDouble(), Is.GreaterThanOrEqualTo(results[1].GetProperty("score").GetDouble()));
            Assert.That(results[0].GetProperty("url").GetString(), Is.EqualTo("/Main/inquiries/" + results[0].GetProperty("id").GetString()));
            Assert.That(results[0].GetProperty("text").GetString(), Does.StartWith("件名: "));
            Assert.That(progress.Texts, Is.Not.Empty);
        }

        [Test]
        public async Task 意味検索できないモジュールと空の問い合わせはエラーを返す()
        {
            var tools = ToolSet().CreateTools(Context()).ToList();
            using var doc = await InvokeAsync(tools, new() { ["moduleName"] = "Plain", ["query"] = "x" });
            Assert.That(doc.RootElement.GetProperty("error").GetString(), Does.Contain("Plain").And.Contain("Inquiry"));
            using var doc2 = await InvokeAsync(tools, new() { ["moduleName"] = "Inquiry", ["query"] = " " });
            Assert.That(doc2.RootElement.TryGetProperty("error", out _), Is.True);
        }

        [Test]
        public void 許されたデータソースに無いモジュールはツールに出ない()
        {
            var toolSet = ToolSet(new List<string> { "Other" });
            Assert.That(toolSet.GetInstructions(Context()), Is.Empty);
            Assert.That(toolSet.CreateTools(Context()), Is.Empty);
        }
    }
}
