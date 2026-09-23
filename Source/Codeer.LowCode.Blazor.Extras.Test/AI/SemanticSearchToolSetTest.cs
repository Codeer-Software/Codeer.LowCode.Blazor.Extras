using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// SemanticSearchToolSet (search_records): 意味検索できるモジュールの判定と AI への説明、エラー応答。
    /// 距離計算は DB (pgvector / SQL Server 2025) が行うので、ここでは接続せずデータソース定義だけで判定を見る
    /// (距離順 SELECT の文字列は SemanticSearchDbSearchTest、実 DB は SemanticSearchRealAITest)。
    /// </summary>
    public class SemanticSearchToolSetTest
    {
        const string Ds = "Main";

        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        static AIChatToolContext Context(Progress? progress = null)
            => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u" }, progress ?? new Progress(), CancellationToken.None, null);

        static DesignData CreateDesign(string vectorSearchColumn = "search_vector_v")
        {
            var d = new DesignData();
            var m = new ModuleDesign { Name = "Inquiry", DataSourceName = Ds, DbTable = "inquiries" };
            m.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            m.Fields.Add(new TextFieldDesign { Name = "Subject", DisplayName = "件名", DbColumn = "subject" });
            m.Fields.Add(new TextFieldDesign { Name = "Body", DisplayName = "本文", DbColumn = "body" });
            m.Fields.Add(new SemanticSearchFieldDesign { Name = "Search", DbColumnText = "search_text", DbColumnVector = "search_vector", DbColumnVectorSearch = vectorSearchColumn });
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

        //接続はしない (DataSource の定義だけ使う) ので接続文字列はダミー
        static DbAccessor Db(DataSourceType type) => new([new DataSource { Name = Ds, DataSourceType = type, ConnectionString = "Host=none" }]);

        static SemanticSearchToolSet ToolSet(DesignData design, DataSourceType type = DataSourceType.PostgreSQL, IList<string>? dataSourceNames = null)
            => new(() => design, () => Db(type), () => new FakeEmbeddingGenerator(), dataSourceNames ?? new List<string> { Ds });

        static async Task<JsonDocument> InvokeAsync(IEnumerable<AITool> tools, Dictionary<string, object?> args)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == "search_records");
            var result = await function.InvokeAsync(new AIFunctionArguments(args));
            var json = result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
            return JsonDocument.Parse(json);
        }

        [Test]
        public void 説明には意味検索できるモジュールと文章にしたフィールドとDBのベクトル列が出る()
        {
            var text = ToolSet(CreateDesign()).GetInstructions(Context());
            Assert.That(text, Does.Contain("Inquiry [問い合わせ]").And.Contain("件名, 本文").And.Contain("search_records"));
            Assert.That(text, Does.Contain("inquiries").And.Contain("search_vector_v").And.Contain("{embed:").And.Contain("<=>"), "execute_sql で使うベクトル列と pgvector の書き方");
            Assert.That(text, Does.Not.Contain("Plain"));

            var sqlServer = ToolSet(CreateDesign(), DataSourceType.SQLServer).GetInstructions(Context());
            Assert.That(sqlServer, Does.Contain("VECTOR_DISTANCE"));
        }

        [Test]
        public void ツールはsearch_recordsだけ()
        {
            var tools = ToolSet(CreateDesign()).CreateTools(Context()).ToList();
            Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "search_records" }));
        }

        [Test]
        public void ベクトル検索に対応しないDBのモジュールは意味検索の対象にならない()
        {
            foreach (var type in new[] { DataSourceType.SQLite, DataSourceType.MySQL, DataSourceType.Oracle })
            {
                var toolSet = ToolSet(CreateDesign(), type);
                Assert.That(toolSet.GetInstructions(Context()), Is.Empty, type.ToString());
                Assert.That(toolSet.CreateTools(Context()), Is.Empty, type.ToString());
            }
        }

        [Test]
        public void ベクトル検索用の列が無いモジュールは意味検索の対象にならない()
        {
            var toolSet = ToolSet(CreateDesign(vectorSearchColumn: ""));
            Assert.That(toolSet.GetInstructions(Context()), Is.Empty);
            Assert.That(toolSet.CreateTools(Context()), Is.Empty);
        }

        [Test]
        public async Task 意味検索できないモジュールと空の問い合わせはエラーを返す()
        {
            var tools = ToolSet(CreateDesign()).CreateTools(Context()).ToList();
            using var doc = await InvokeAsync(tools, new() { ["moduleName"] = "Plain", ["query"] = "x" });
            Assert.That(doc.RootElement.GetProperty("error").GetString(), Does.Contain("Plain").And.Contain("Inquiry"));
            using var doc2 = await InvokeAsync(tools, new() { ["moduleName"] = "Inquiry", ["query"] = " " });
            Assert.That(doc2.RootElement.TryGetProperty("error", out _), Is.True);
        }

        [Test]
        public void 許されたデータソースに無いモジュールはツールに出ない()
        {
            var toolSet = ToolSet(CreateDesign(), dataSourceNames: new List<string> { "Other" });
            Assert.That(toolSet.GetInstructions(Context()), Is.Empty);
            Assert.That(toolSet.CreateTools(Context()), Is.Empty);
        }
    }
}
