using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.AI;
using Codeer.LowCode.Blazor.Repository.Design;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.AI
{
    /// <summary>
    /// AITextAnalyzeService がモデルに渡す形: 抽出結果の JSON スキーマ (キーをフィールド名に固定) と、画像として渡せるファイルの判定。
    /// 表示名が同じフィールド (規格の「記号」と結果の「記号」) があっても、キーはフィールド名で別になることを固定する。
    /// </summary>
    public class AITextAnalyzeExtractionTest
    {
        static DesignData CreateDesign()
        {
            var design = new DesignData();

            var item = new ModuleDesign { Name = "Item" };
            item.Fields.Add(new IdFieldDesign { Name = "Id" });
            item.Fields.Add(new TextFieldDesign { Name = "SpecSign", DisplayName = "記号" });
            item.Fields.Add(new TextFieldDesign { Name = "ResultSign", DisplayName = "記号" });
            item.Fields.Add(new NumberFieldDesign { Name = "Value", DisplayName = "Value" });
            design.AddModule(item);

            var sheet = new ModuleDesign { Name = "Sheet" };
            sheet.Fields.Add(new TextFieldDesign { Name = "LotNo", DisplayName = "ロット番号" });
            sheet.Fields.Add(new BooleanFieldDesign { Name = "Passed" });
            sheet.Fields.Add(new DateFieldDesign { Name = "IssueDate" });
            sheet.Fields.Add(new LabelFieldDesign { Name = "Title" });
            var items = new ListFieldDesign { Name = "Items" };
            items.SearchCondition.ModuleName = "Item";
            sheet.Fields.Add(items);
            design.AddModule(sheet);
            return design;
        }

        [Test]
        public void スキーマのキーはフィールド名で表示名は説明になる()
        {
            var schema = AITextAnalyzeService.CreateJsonSchema(CreateDesign().Modules, "Sheet");

            Assert.That(schema.GetProperty("type").GetString(), Is.EqualTo("object"));
            Assert.That(schema.GetProperty("additionalProperties").GetBoolean(), Is.False);
            var properties = schema.GetProperty("properties");
            Assert.That(properties.EnumerateObject().Select(e => e.Name), Is.EqualTo(new[] { "LotNo", "Passed", "IssueDate", "Items" }), "値を入れられない型 (Label) は出さない");
            Assert.That(schema.GetProperty("required").EnumerateArray().Select(e => e.GetString()), Is.EqualTo(new[] { "LotNo", "Passed", "IssueDate", "Items" }));
            Assert.That(properties.GetProperty("LotNo").GetProperty("description").GetString(), Is.EqualTo("ロット番号"));
            Assert.That(Types(properties.GetProperty("LotNo")), Is.EqualTo(new[] { "string", "null" }));
            Assert.That(Types(properties.GetProperty("Passed")), Is.EqualTo(new[] { "boolean", "null" }));
            Assert.That(Types(properties.GetProperty("IssueDate")), Is.EqualTo(new[] { "string", "null" }));
        }

        [Test]
        public void 子モジュールは配列で表示名が同じフィールドも別のキーになる()
        {
            var schema = AITextAnalyzeService.CreateJsonSchema(CreateDesign().Modules, "Sheet");

            var items = schema.GetProperty("properties").GetProperty("Items");
            Assert.That(items.GetProperty("type").GetString(), Is.EqualTo("array"));
            var child = items.GetProperty("items");
            Assert.That(child.GetProperty("additionalProperties").GetBoolean(), Is.False);
            var properties = child.GetProperty("properties");
            Assert.That(properties.EnumerateObject().Select(e => e.Name), Is.EqualTo(new[] { "Id", "SpecSign", "ResultSign", "Value" }));
            Assert.That(properties.GetProperty("SpecSign").GetProperty("description").GetString(), Is.EqualTo("記号"));
            Assert.That(properties.GetProperty("ResultSign").GetProperty("description").GetString(), Is.EqualTo("記号"));
            Assert.That(Types(properties.GetProperty("Value")), Is.EqualTo(new[] { "number", "string", "null" }), "数値は帳票の表記のまま文字列でも受ける");
            Assert.That(properties.GetProperty("Value").TryGetProperty("description", out _), Is.False, "フィールド名と同じ表示名は説明に出さない");
        }

        [Test]
        public void 画像として渡すのはjpeg_png_gif_webpだけ()
        {
            Assert.That(AITextAnalyzeService.GetImageMediaType(Bytes(0xFF, 0xD8, 0xFF, 0xE0)), Is.EqualTo("image/jpeg"));
            Assert.That(AITextAnalyzeService.GetImageMediaType(Bytes(0x89, 0x50, 0x4E, 0x47)), Is.EqualTo("image/png"));
            Assert.That(AITextAnalyzeService.GetImageMediaType(Bytes(0x47, 0x49, 0x46, 0x38)), Is.EqualTo("image/gif"));
            Assert.That(AITextAnalyzeService.GetImageMediaType(Bytes(0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50)), Is.EqualTo("image/webp"));
            Assert.That(AITextAnalyzeService.GetImageMediaType(Bytes(0x25, 0x50, 0x44, 0x46)), Is.Null, "PDF は文書解析の結果だけを渡す");
            Assert.That(AITextAnalyzeService.GetImageMediaType(new byte[] { 0xFF, 0xD8, 0xFF }), Is.Null);

            using var jpeg = new MemoryStream(Bytes(0xFF, 0xD8, 0xFF, 0xE0));
            var content = AITextAnalyzeService.CreateImageContent(jpeg);
            Assert.That(content?.MediaType, Is.EqualTo("image/jpeg"));
            Assert.That(content!.Data.Length, Is.EqualTo(12));

            using var pdf = new MemoryStream(Bytes(0x25, 0x50, 0x44, 0x46));
            Assert.That(AITextAnalyzeService.CreateImageContent(pdf), Is.Null);
        }

        [Test]
        public async Task スキーマはstrictでOpenAIに送られる()
        {
            var handler = new CaptureHandler();
            var client = new OpenAIClient(new ApiKeyCredential("dummy"), new OpenAIClientOptions { Transport = new HttpClientPipelineTransport(new HttpClient(handler)) })
                .GetChatClient("model").AsIChatClient();

            await client.GetResponseAsync("text", AITextAnalyzeService.CreateExtractionOptions(CreateDesign().Modules, "Sheet", true));

            var format = JsonDocument.Parse(handler.Body!).RootElement.GetProperty("response_format");
            Assert.That(format.GetProperty("type").GetString(), Is.EqualTo("json_schema"));
            Assert.That(format.GetProperty("json_schema").GetProperty("strict").GetBoolean(), Is.True);
            Assert.That(format.GetProperty("json_schema").GetProperty("schema").GetProperty("properties").TryGetProperty("Items", out _), Is.True);
        }

        [Test]
        public async Task 抽出は常にスキーマ付きで呼ぶ()
        {
            var chat = new FakeChatClient().Text("""{"LotNo":"A1"}""");

            var data = await new TestService(chat).AnalyzeAsync(CreateDesign().Modules, "Sheet");

            Assert.That(chat.Options, Has.Count.EqualTo(1));
            Assert.That((chat.Options[0]!.ResponseFormat as ChatResponseFormatJson)?.Schema, Is.Not.Null);
            Assert.That((data.Fields["LotNo"] as Codeer.LowCode.Blazor.Repository.Data.TextFieldData)?.Value, Is.EqualTo("A1"));
        }

        [Test]
        public async Task スキーマ付きで失敗したらスキーマなしでやり直す()
        {
            var chat = new FakeChatClient().Text("not json").Text("""{"LotNo":"A1"}""");

            var data = await new TestService(chat).AnalyzeAsync(CreateDesign().Modules, "Sheet");

            Assert.That(chat.Options, Has.Count.EqualTo(2));
            Assert.That((chat.Options[0]!.ResponseFormat as ChatResponseFormatJson)?.Schema, Is.Not.Null);
            Assert.That(chat.Options[1]!.ResponseFormat, Is.InstanceOf<ChatResponseFormatJson>());
            Assert.That(((ChatResponseFormatJson)chat.Options[1]!.ResponseFormat!).Schema, Is.Null);
            Assert.That((data.Fields["LotNo"] as Codeer.LowCode.Blazor.Repository.Data.TextFieldData)?.Value, Is.EqualTo("A1"));
        }

        class TestService(IChatClient chat) : AITextAnalyzeService(new AISettings(), () => chat)
        {
            //候補の解決 (Select / Link) を通らないデザインなので ModuleDataIO は使われない
            public Task<Codeer.LowCode.Blazor.Repository.Data.ModuleData> AnalyzeAsync(IModuleDesigns modules, string moduleName)
                => AnalyzeCoreAsync(null!, modules, moduleName, "", "text", "");
        }

        class CaptureHandler : HttpMessageHandler
        {
            public string? Body { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Body = await request.Content!.ReadAsStringAsync(cancellationToken);
                const string response = """{"id":"x","object":"chat.completion","created":0,"model":"model","choices":[{"index":0,"message":{"role":"assistant","content":"{}"},"finish_reason":"stop"}]}""";
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
            }
        }

        static string?[] Types(JsonElement property) => property.GetProperty("type").EnumerateArray().Select(e => e.GetString()).ToArray();

        static byte[] Bytes(params byte[] head)
        {
            var bytes = new byte[12];
            head.CopyTo(bytes, 0);
            return bytes;
        }
    }
}
