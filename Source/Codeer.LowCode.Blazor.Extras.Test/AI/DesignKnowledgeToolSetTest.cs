using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.ChatClient;
using Codeer.LowCode.Blazor.Extras.Server.AI.Chat.DesignKnowledge;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>DesignKnowledgeToolSet: デザイン定義の一覧・詳細 (候補値・Enum・リンク・論理削除・スクリプト) と補足文書。</summary>
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

        [Test]
        public async Task DBの名前を出さない指定では_データソースと表と列とSQL向けの注意が出ない()
        {
            var design = CreateDesign();
            design.Modules.Find("Order")!.DataReadCondition = new() { Condition = new Codeer.LowCode.Blazor.Repository.Match.FieldValueMatchCondition() };
            var toolSet = new DesignKnowledgeToolSet(() => design, null, showDbNames: false);
            var tools = toolSet.CreateTools(Context()).ToList();

            var all = toolSet.GetInstructions(Context())
                + await InvokeAsync(tools, "list_modules")
                + await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" })
                + await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Customer" })
                + string.Join("\n", tools.OfType<AIFunction>().Select(t => t.Description));

            //DB の名前 (データソース・表・列) と、SQL を書くための注意は出ない
            foreach (var hidden in new[] { "表 orders", "表: orders", "customers", "customer_id", "is_deleted", "amount", "status", "kind", "@ Main", "データソース", "DB 列", "論理削除", "行の閲覧条件", "SQL", "get_schema" })
                Assert.That(all, Does.Not.Contain(hidden), hidden);

            //項目・候補値・リンク・スクリプト・画面 URL は出る
            var order = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" });
            Assert.That(order, Does.Contain("- Amount [金額] Number"));
            Assert.That(order, Does.Contain("候補値: 1=受付, 9=完了"));
            Assert.That(order, Does.Contain("リンク → Customer.Id (表示は Name)"));
            Assert.That(order, Does.Contain("Amount_OnDataChanged"));
            Assert.That(order, Does.Contain("詳細 /Main/orders/{Id}"));
        }

        [Test]
        public async Task 見せないモジュールは_一覧にも詳細にもリンク先にも出ない()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null, showDbNames: true, isVisible: (module, _) => module.Name != "Customer").CreateTools(Context()).ToList();

            var list = await InvokeAsync(tools, "list_modules");
            Assert.That(list, Does.Contain("- Order"));
            Assert.That(list, Does.Not.Contain("Customer"));

            //無いモジュールと同じ答え
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Customer" }), Does.Contain("モジュール 'Customer' はありません"));
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "得意先" }), Does.Contain("はありません"));

            //リンクの相手としても案内しない (項目自体は残る)
            var order = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" });
            Assert.That(order, Does.Contain("- Customer [得意先] Link"));
            Assert.That(order, Does.Not.Contain("リンク →"));
        }

        sealed class UrlResolver : IPageLinkUrlResolver
        {
            public string GetModuleUrl(string currentPageFrame, PageLink pageLink) => "/" + currentPageFrame + "/" + pageLink.Module;
        }

        [Test]
        public async Task 画面用に送るデザインを渡すと_ブラウザが受け取る範囲だけが見える()
        {
            //サーバーが持つデザイン (SQL 本文とジョブのスクリプト入り) から、ホストがブラウザへ返すのと同じ手順で画面用のデザインを作る
            var server = CreateDesign();
            server.Modules.Find("Order")!.Fields.OfType<QueryFieldDesign>().Single().QuerySetting.Parameters.Add(new DbParameterSetting { Name = "secret_param", DbType = "text", IsParameter = true });
            server.JobScripts["Nightly"] = "void Run() { /* job-secret */ }";
            var transfer = Codeer.LowCode.Blazor.DesignLogic.Transfer.DesignDataTransferLogic.CreateTransferDesignData(server);
            var bytes = Codeer.LowCode.Blazor.DesignLogic.Transfer.DesignDataTransferLogic.ToBinary(
                Codeer.LowCode.Blazor.DesignLogic.Transfer.DesignDataTransferLogic.AddResolvedPageFrames(transfer, server.ResolvePageFrames(new UrlResolver(), null)));
            var forFront = Codeer.LowCode.Blazor.DesignLogic.Transfer.DesignDataTransferLogic.ToDesignData(new MemoryStream(bytes));

            //渡したデザインに DB の名前は残っていない (本体が伏せている)
            Assert.That(forFront.Modules.Find("Order")!.DbTable, Is.EqualTo("*"));
            Assert.That(forFront.JobScripts, Is.Empty);

            var users = new List<string>();
            var toolSet = new DesignKnowledgeToolSet(context => { users.Add(context.Request.UserName); return Task.FromResult<DesignData?>(forFront); }, null, showDbNames: false);
            var tools = toolSet.CreateTools(Context()).ToList();

            var list = await InvokeAsync(tools, "list_modules");
            var order = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" });
            var customer = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Customer" });
            var all = list + order + customer;

            foreach (var hidden in new[] { "表 orders", "customers", "customer_id", "is_deleted", "secret_param", "job-secret", "表 *", "表: *", "列 *", "@ *", "データソース" })
                Assert.That(all, Does.Not.Contain(hidden), hidden);

            //項目・候補値・リンク・スクリプト・画面の呼び名と URL は出る
            Assert.That(list, Does.Contain("- Order (受注)"));
            Assert.That(order, Does.Contain("- Amount [金額] Number"));
            Assert.That(order, Does.Contain("候補値: 1=受付, 9=完了"));
            Assert.That(order, Does.Contain("リンク → Customer.Id (表示は Name)"));
            Assert.That(order, Does.Contain("Amount_OnDataChanged"));
            Assert.That(order, Does.Contain("詳細 /Main/orders/{Id}"));

            //依頼したユーザーで取り、1 回の返事の中では取り直さない
            Assert.That(users, Is.EqualTo(new[] { "u" }));
        }

        [Test]
        public async Task デザインを取れなければ_設計が読めないと答える()
        {
            var tools = new DesignKnowledgeToolSet((AIChatToolContext _) => throw new InvalidOperationException("x"), null, showDbNames: false).CreateTools(Context()).ToList();
            Assert.That(await InvokeAsync(tools, "list_modules"), Does.Contain("設計が読めません"));
        }

        [Test]
        public void DB直版が設計として見せるのは_読めるデータソースのモジュールと表を持たないモジュール()
        {
            var readable = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AIReadOnly" };
            bool IsReadable(string dataSource, string table)
                => Codeer.LowCode.Blazor.Extras.Server.AI.Chat.RawDataAccess.RawDataAccessAgent.IsReadable(new ModuleDesign { Name = "M", DataSourceName = dataSource, DbTable = table }, readable);

            Assert.That(IsReadable("AIReadOnly", "orders"), Is.True);
            Assert.That(IsReadable("aireadonly", "orders"), Is.True);
            Assert.That(IsReadable("Main", "orders"), Is.False);
            Assert.That(IsReadable("", "orders"), Is.False);
            Assert.That(IsReadable("Main", ""), Is.True);   //表なし = 画面のロジック
        }

        static DesignData CreateDesign()
        {
            var design = new DesignData();
            design.Enums.Add(new EnumDesign
            {
                Name = "OrderStatus",
                Members = { new EnumMemberDesign { Name = "Open", Value = "1", DisplayText = "受付" }, new EnumMemberDesign { Name = "Closed", Value = "9", DisplayText = "完了" } },
            });

            var customer = new ModuleDesign { Name = "Customer", DataSourceName = "Main", DbTable = "customers" };
            customer.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            customer.Fields.Add(new TextFieldDesign { Name = "Name", DisplayName = "得意先名", DbColumn = "name", IsRequired = true });
            design.AddModule(customer);

            var order = new ModuleDesign { Name = "Order", DataSourceName = "Main", DbTable = "orders" };
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
            order.Fields.Add(new QueryFieldDesign { Name = "MonthlyTotal" });   //SQL は .sql ファイル側。describe_module は型だけ出す
            design.AddModule(order);
            design.Scripts["Order"] = "void Amount_OnDataChanged() { Total.Value = Amount.Value * 1.1m; }";

            //ページフレーム Main のサイドバーに Order へのリンク (表示名「受注」= モジュールの呼び名になる。URL セグメントは orders)。
            //Customer はリンクではなく「その他のページ」(一覧ページのタイトル「得意先」が呼び名。セグメント未指定 → モジュール名)
            var main = new PageFrameDesign { Name = "Main", IsApplicationRoot = true };
            main.Left.Links.Add(new PageLink { Title = "受注", Module = "Order", ModuleUrlSegment = "orders", ModulePageType = ModulePageType.List });
            main.OtherPageModuleDesigns.Add(new ModulePageDesign { Module = "Customer", ListPageDesign = { PageTitle = "得意先" } });
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
            var tools = new DesignKnowledgeToolSet(() => design, null, showDbNames: true).CreateTools(Context()).ToList();
            Assert.That(tools.Select(t => t.Name), Is.EqualTo(new[] { "list_modules", "describe_module" }), "文書が無ければ read_document は付かない");

            var list = await InvokeAsync(tools, "list_modules");
            Assert.That(list, Does.Contain("- Customer (得意先): 表 customers @ Main; URL /Main/Customer"));
            Assert.That(list, Does.Contain("- Order (受注): 表 orders @ Main; リンク先: Customer; URL /Main/orders"));
        }

        [Test]
        public async Task describe_moduleはフィールドと候補値とリンクと論理削除とスクリプトを返す()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null, showDbNames: true).CreateTools(Context()).ToList();
            var text = await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Order" });

            Assert.That(text, Does.Contain("# モジュール Order (受注)"));
            Assert.That(text, Does.Contain("表: orders (データソース Main)"));
            Assert.That(text, Does.Contain("論理削除: 列 is_deleted"));
            Assert.That(text, Does.Contain("画面 URL: 一覧 /Main/orders / 詳細 /Main/orders/{Id} ({Id} は列 id の値)"));
            Assert.That(text, Does.Contain("- Amount [金額] Number, 列 amount"));
            Assert.That(text, Does.Contain("候補値: 1=受付, 9=完了"), "Enum 参照の候補値");
            Assert.That(text, Does.Contain("候補値: N=通常, S=特注"), "Candidates (表示,値) の候補値");
            Assert.That(text, Does.Contain("リンク → Customer.Id (表示は Name)"));
            Assert.That(text, Does.Contain("- MonthlyTotal Query"));
            Assert.That(text, Does.Not.Contain("```sql"), "Query の SQL は実行エンジン専用のバッファなので出さない");
            Assert.That(text, Does.Contain("Amount_OnDataChanged"));
        }

        [Test]
        public async Task describe_moduleは表示名や大文字小文字の違いも寄せ無ければ案内を返す()
        {
            var design = CreateDesign();
            var tools = new DesignKnowledgeToolSet(() => design, null, showDbNames: true).CreateTools(Context()).ToList();
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "受注" }), Does.Contain("# モジュール Order"));
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "customer" }), Does.Contain("# モジュール Customer"));
            Assert.That(await InvokeAsync(tools, "describe_module", new() { ["moduleName"] = "Nope" }), Does.Contain("list_modules"));
        }

        [Test]
        public async Task 文書は一覧が説明に入りread_documentで本文が読める()
        {
            var toolSet = new DesignKnowledgeToolSet((Func<DesignData?>?)null, Documents, showDbNames: true);
            var instructions = toolSet.GetInstructions(Context());
            Assert.That(instructions, Does.Contain("### 文書: 用語集"), "小さい文書は全文がプロンプトに入る");
            Assert.That(instructions, Does.Contain("売上 = 受注の金額の合計"));
            Assert.That(instructions, Does.Not.Contain("list_modules"), "設計が無ければ設計の説明は入らない");

            var large = new DesignKnowledgeToolSet((Func<DesignData?>?)null, Documents, showDbNames: true, inlineDocumentsMaxChars: 10, excerptChars: 12);
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
