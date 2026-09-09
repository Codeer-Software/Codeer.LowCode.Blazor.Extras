using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>DesignKnowledgeToolSet: デザイン定義の一覧・詳細 (候補値・Enum・リンク・論理削除・Query SQL・スクリプト) と補足文書。</summary>
    public class DesignKnowledgeToolSetTest
    {
        sealed class Progress : IAIChatProgress
        {
            public List<string> Texts { get; } = new();
            public void Report(string progressText) => Texts.Add(progressText);
            public void ReportPartial(AIChatReply partialReply) { }
        }

        static AIChatToolContext Context(string documentFolder = "AIChat") => new(new AIChatAgentRequest { ConversationId = "c", Message = "m", UserName = "u", DocumentFolder = documentFolder }, new Progress(), CancellationToken.None, null);

        static async Task<string> InvokeAsync(IEnumerable<AITool> tools, string name, Dictionary<string, object?>? args = null)
        {
            var function = tools.OfType<AIFunction>().Single(t => t.Name == name);
            var result = await function.InvokeAsync(new AIFunctionArguments(args ?? new Dictionary<string, object?>()));
            return result is JsonElement e ? e.GetString() ?? e.ToString() : result?.ToString() ?? string.Empty;
        }

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            design.Enums.Add(new EnumDesign
            {
                Name = "OrderStatus",
                Members = { new EnumMemberDesign { Name = "Open", Value = "1", DisplayText = "受付" }, new EnumMemberDesign { Name = "Closed", Value = "9", DisplayText = "完了" } },
            });

            var customer = new ModuleDesign { Name = "Customer", PageTitle = "得意先", DataSourceName = "Main", DbTable = "customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "得意先名", DbColumn = "name", IsRequired = true });
            design.AddModule(customer);

            var order = new ModuleDesign { Name = "Order", PageTitle = "受注", DataSourceName = "Main", DbTable = "orders" };
            order.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            order.Fields.Add(new BooleanFieldDesign { Name = "LogicalDelete", DbColumn = "is_deleted" });
            order.Fields.Add(new NumberFieldDesign { Name = "Amount", DisplayName = "金額", DbColumn = "amount" });
            order.Fields.Add(new SelectFieldDesign { Name = "Status", DisplayName = "状態", DbColumn = "status", EnumName = "OrderStatus" });
            var kind = new SelectFieldDesign { Name = "Kind", DbColumn = "kind" };
            kind.Candidates.AddRange(new[] { "通常,N", "特注,S" });
            order.Fields.Add(kind);
            var link = new LinkFieldDesign { Name = "Customer", DisplayName = "得意先", DbColumn = "customer_id", ValueVariable = "Id", DisplayTextVariable = "Name" };
            link.SearchCondition.ModuleName = "Customer";
            order.Fields.Add(link);
            var query = new QueryFieldDesign { Name = "MonthlyTotal" };
            query.QuerySetting.QueryText = "SELECT strftime('%Y-%m', order_date) AS ym, SUM(amount) FROM orders WHERE is_deleted = 0 GROUP BY ym";
            order.Fields.Add(query);
            design.AddModule(order);
            design.Scripts["Order"] = "void Amount_OnDataChanged() { Total.Value = Amount.Value * 1.1m; }";

            //ページフレーム Main のサイドバーに Order へのリンク (URL セグメントは orders)。Customer にはリンクが無い → ルートのフレームの下で組む
            var main = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            main.Left.Links.Add(new PageLink { Title = "受注", Module = "Order", ModuleUrlSegment = "orders", ModulePageType = ModulePageType.List });
            ((IEditablePageFrameDesign)design.PageFrames).Add(main);
            return design;
        }

        //フォルダごとの文書 (AIChat: 用語集 + 締め / Other: 別の文書 / それ以外: 無し)
        static IReadOnlyList<AIChatDocument> Documents(string folder) => folder switch
        {
            "AIChat" => new[]
            {
                new AIChatDocument("用語集", "# 用語集\n\n売上 = 受注の金額の合計 (状態が完了のものだけ)。"),
                new AIChatDocument("締め", "# 締め日\n\n毎月 20 日締め。"),
            },
            "Other" => new[] { new AIChatDocument("在庫", "# 在庫\n\n在庫 = 入庫 - 出庫。") },
            _ => Array.Empty<AIChatDocument>(),
        };

        [Test]
        public async Task list_modulesは表とリンク先を含む一覧を返す()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null).CreateTools(Context()).ToList();
            Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "list_modules", "describe_module" }), "文書が無ければ read_document は付かない");

            var list = await InvokeAsync(tools, "list_modules");
            Assert.That(list, Does.Contain("- Customer (得意先): 表 customers @ Main; URL /Main/Customer"));
            Assert.That(list, Does.Contain("- Order (受注): 表 orders @ Main; リンク先: Customer; URL /Main/orders"));
        }

        [Test]
        public async Task describe_moduleはフィールドと候補値とリンクと論理削除とSQLとスクリプトを返す()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null).CreateTools(Context()).ToList();
            var text = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" });

            Assert.That(text, Does.Contain("# モジュール Order (受注)"));
            Assert.That(text, Does.Contain("表: orders (データソース Main)"));
            Assert.That(text, Does.Contain("論理削除: 列 is_deleted"));
            Assert.That(text, Does.Contain("画面 URL: 一覧 /Main/orders / 詳細 /Main/orders/{Id} ({Id} は列 id の値)"));
            Assert.That(text, Does.Contain("- Amount [金額] Number, 列 amount"));
            Assert.That(text, Does.Contain("候補値: 1=受付, 9=完了"), "Enum 参照の候補値");
            Assert.That(text, Does.Contain("候補値: N=通常, S=特注"), "Candidates (表示,値) の候補値");
            Assert.That(text, Does.Contain("リンク → Customer.Id (表示は Name)"));
            Assert.That(text, Does.Contain("```sql"));
            Assert.That(text, Does.Contain("GROUP BY ym"));
            Assert.That(text, Does.Contain("Amount_OnDataChanged"));
        }

        [Test]
        public async Task describe_moduleは表示名や大文字小文字の違いも寄せ無ければ案内を返す()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null).CreateTools(Context()).ToList();
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "受注" }), Does.Contain("# モジュール Order"));
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "customer" }), Does.Contain("# モジュール Customer"));
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Nope" }), Does.Contain("list_modules"));
        }

        [Test]
        public async Task 文書は一覧が説明に入りread_documentで本文が読める()
        {
            var toolSet = new DesignKnowledgeToolSet(null, Documents);
            var instructions = toolSet.GetInstructions(Context());
            Assert.That(instructions, Does.Contain("### 文書: 用語集"), "小さい文書は全文がプロンプトに入る");
            Assert.That(instructions, Does.Contain("売上 = 受注の金額の合計"));
            Assert.That(instructions, Does.Not.Contain("list_modules"), "設計が無ければ設計の説明は入らない");

            var large = new DesignKnowledgeToolSet(null, Documents, inlineDocumentsMaxChars: 10, excerptChars: 12);
            Assert.That(large.GetInstructions(Context()), Does.Contain("- 用語集: 用語集 売上 = 受注の…"), "大きければ一覧と抜粋");
            Assert.That(large.GetInstructions(Context()), Does.Not.Contain("### 文書"));

            //フォルダはフィールドごと: 別フォルダなら別の文書、空なら文書なし (プロンプトにもツールにも出ない)
            Assert.That(toolSet.GetInstructions(Context("Other")), Does.Contain("### 文書: 在庫"));
            Assert.That(toolSet.GetInstructions(Context("Other")), Does.Not.Contain("用語集"));
            Assert.That(toolSet.GetInstructions(Context("")), Is.Empty);
            Assert.That(toolSet.CreateTools(Context("")).Select(t => t.Name), Is.Empty);

            var tools = toolSet.CreateTools(Context()).ToList();
            Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "read_document" }));
            var text = await InvokeAsync(tools, "read_document", new() { ["name"] = "用語集" });
            Assert.That(text, Does.Contain("売上 = 受注の金額の合計"));
            var missing = await InvokeAsync(tools, "read_document", new() { ["name"] = "ない" });
            Assert.That(missing, Does.Contain("用語集, 締め"));
        }

        [Test]
        public void スキーマ説明の候補値はEnumとCandidatesの両方を解く()
        {
            var design = CreateDesign();
            var order = design.Modules.Find("Order")!;
            Assert.That(DesignDescriber.Candidates(design, order.Fields.First(f => f.Name == "Status")), Is.EqualTo(new[] { "1=受付", "9=完了" }));
            Assert.That(DesignDescriber.Candidates(design, order.Fields.First(f => f.Name == "Kind")), Is.EqualTo(new[] { "N=通常", "S=特注" }));
            Assert.That(DesignDescriber.Candidates(design, order.Fields.First(f => f.Name == "Amount")), Is.Empty);
        }
    }
}
