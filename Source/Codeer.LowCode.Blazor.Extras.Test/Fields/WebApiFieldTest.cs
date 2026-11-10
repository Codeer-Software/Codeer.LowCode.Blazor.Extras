using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.Extras.Test.Harness;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.RequestInterfaces;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Test.Fields
{
    /// <summary>
    /// WebApiField のランタイム: スクリプトの Execute (送る内容・キーの先頭の揃え方・戻り値・応答のボディの JsonObject 化)、ボタン (引数なし・失敗の通知・OnCompleted)、
    /// JsonField との結線 (ArgumentsField の内容を送る・ResultField に応答を入れる)、実行中の二重実行、押せない状態。
    /// ホスト (ExtrasTransport.Handler) と通知 (IUIService) は記録するだけの偽物。
    /// </summary>
    [TestFixture, NonParallelizable]
    public class WebApiFieldTest
    {
        class RecordingUIService : IUIService
        {
            public List<string> Successes { get; } = new();
            public List<string> Errors { get; } = new();
            public Task NotifySuccess(string message) { Successes.Add(message); return Task.CompletedTask; }
            public Task NotifyError(string message) { Errors.Add(message); return Task.CompletedTask; }
            public Task CloseDialog(Module moduleData, string button) => Task.CompletedTask;
            public Task ClosePanel(Module module, string button) => Task.CompletedTask;
            public Task ClosePopup(Module module, string button) => Task.CompletedTask;
            public Task<string> ShowDialog(Module moduleData, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPopup(Module moduleData, int x, int y, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPanel(Module moduleData, DialogButton[] buttons, PanelAlignment alignment) => Task.FromResult(string.Empty);
            public Task<string> ShowMessageBox(string title, string message, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<bool> DownloadFile(MemoryStream stream, string name) => Task.FromResult(true);
        }

        class Host
        {
            public RecordingUIService UI { get; init; } = null!;
            public List<WebApiExecuteRequest> Requests { get; } = new();
            public Func<WebApiExecuteRequest, Task<WebApiExecuteResponse?>> OnExecute { get; set; }
                = _ => Task.FromResult<WebApiExecuteResponse?>(new WebApiExecuteResponse { StatusCode = 200, Body = """{"name":"abc"}""" });
        }

        class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        Func<ExtrasRequest, Task<ExtrasResponse?>>? _savedExtras;

        [SetUp]
        public void SetUp() => _savedExtras = ExtrasTransport.Handler;

        [TearDown]
        public void TearDown() => ExtrasTransport.Handler = _savedExtras;

        static GridRow Row(string fieldName)
            => new() { Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = fieldName } } } };

        //表を持つモジュール (Id + Name + Api + Result + JsonField 4 つとそのノードのフィールド)。script はモジュールのスクリプト (OnCompleted 用)
        //Args (JsonField): ノード title (Text) / qty (Number) を ArgTitle / ArgQty に結線
        //Res (JsonField): ノード name (Text) / age (Number) を ResName / ResAge に結線
        //Users (JsonField。UseNodeValueAsJson): ノード items (Array) を一覧 UserRows に結線 (JSON は配列そのもの)
        //Wrapped (JsonField): ノード items (Array) を一覧 WrappedRows に結線 ({"items":[...]})
        //一覧の行 UserRow: Json (ノード name / age) を RowName / RowAge に結線
        static async Task<(Module Module, WebApiField Api, Host Host)> CreateAsync(
            bool nameRequired = false, string onCompleted = "", string script = "",
            string argumentsField = "", string resultField = "", bool upperCamelCaseNames = false)
        {
            var design = new ModuleDesign { Name = "Item", DataSourceName = "Test", DbTable = "T", CanCreate = true, CanUpdate = true };
            design.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "Id" });
            design.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "Name", IsRequired = nameRequired });
            design.Fields.Add(new TextFieldDesign { Name = "Result" });
            design.Fields.Add(new WebApiFieldDesign
            {
                Name = "Api", ConnectionName = "Crm", Path = "users/{id}", OnCompleted = onCompleted,
                ArgumentsField = argumentsField, ResultField = resultField, UseUpperCamelCaseNames = upperCamelCaseNames,
            });
            var args = new JsonFieldDesign { Name = "Args", DbColumn = "args" };
            args.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "title", NodeType = JsonNodeType.Text });
            args.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "qty", NodeType = JsonNodeType.Number });
            design.Fields.Add(args);
            design.Fields.Add(new TextFieldDesign { Name = "ArgTitle", DbColumn = "Args/title" });
            design.Fields.Add(new NumberFieldDesign { Name = "ArgQty", DbColumn = "Args/qty" });
            var res = new JsonFieldDesign { Name = "Res", DbColumn = "res" };
            res.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "name", NodeType = JsonNodeType.Text });
            res.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "age", NodeType = JsonNodeType.Number });
            design.Fields.Add(res);
            design.Fields.Add(new TextFieldDesign { Name = "ResName", DbColumn = "Res/name" });
            design.Fields.Add(new NumberFieldDesign { Name = "ResAge", DbColumn = "Res/age" });
            //Users (JsonField。UseNodeValueAsJson。ノード items: Array) + UserRows (一覧 "Users/items")
            var users = new JsonFieldDesign { Name = "Users", DbColumn = "users", UseNodeValueAsJson = true };
            users.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "items", NodeType = JsonNodeType.Array });
            design.Fields.Add(users);
            design.Fields.Add(new ListFieldDesign { Name = "UserRows", DbColumn = "Users/items", SearchCondition = { ModuleName = "UserRow", LimitCount = null }, CanCreate = true, CanUpdate = true, CanDelete = true });
            //Wrapped (JsonField。ノード items: Array) + WrappedRows (一覧 "Wrapped/items")
            var wrapped = new JsonFieldDesign { Name = "Wrapped", DbColumn = "wrapped" };
            wrapped.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "items", NodeType = JsonNodeType.Array });
            design.Fields.Add(wrapped);
            design.Fields.Add(new ListFieldDesign { Name = "WrappedRows", DbColumn = "Wrapped/items", SearchCondition = { ModuleName = "UserRow", LimitCount = null }, CanCreate = true, CanUpdate = true, CanDelete = true });
            design.DetailLayouts[string.Empty].Layout = new GridLayoutDesign
            {
                Rows = [Row("Name"), Row("Result"), Row("Api"), Row("Args"), Row("ArgTitle"), Row("ArgQty"), Row("Res"), Row("ResName"), Row("ResAge"),
                    Row("Users"), Row("UserRows"), Row("Wrapped"), Row("WrappedRows")]
            };
            //一覧の行 (テーブルなし。JsonField が要素の形 {name: Text, age: Number} を決める)
            var row = new ModuleDesign { Name = "UserRow" };
            var rowJson = new JsonFieldDesign { Name = "Json" };
            rowJson.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "name", NodeType = JsonNodeType.Text });
            rowJson.JsonFieldSettings.JsonNodes.Add(new JsonFieldNodeSettings { Key = "age", NodeType = JsonNodeType.Number });
            row.Fields.Add(rowJson);
            row.Fields.Add(new TextFieldDesign { Name = "RowName", DbColumn = "name" });
            row.Fields.Add(new NumberFieldDesign { Name = "RowAge", DbColumn = "age" });
            row.ListLayouts[string.Empty].Elements = [[new ListElement { FieldName = "RowName" }, new ListElement { FieldName = "RowAge" }]];
            var designData = new DesignData();
            designData.AddModule(design);
            designData.AddModule(row);
            if (!string.IsNullOrEmpty(script)) designData.Scripts["Item"] = script;

            var app = new HarnessAppService(designData);
            app.GetScriptRuntimeTypeManager().AddType<WebApiResult>();
            var ui = new RecordingUIService();
            var host = new Host { UI = ui };
            ExtrasTransport.Handler = async envelope =>
            {
                Assert.That(envelope.Feature, Is.EqualTo(WebApiNames.Feature));
                Assert.That(envelope.Action, Is.EqualTo(WebApiNames.Execute));
                var request = JsonConverterEx.DeserializeObject<WebApiExecuteRequest>(envelope.Payload)!;
                host.Requests.Add(request);
                var response = await host.OnExecute(request);
                return response == null ? null : new ExtrasResponse { Payload = JsonConverterEx.SerializeObject(response, indented: false) };
            };
            var core = new Codeer.LowCode.Blazor.RequestInterfaces.Services(new NullServiceProvider(), app, app, new DummyNavigationService(), ui, new ListLogger());
            var module = await ModuleCreationService.CreateModuleAsync(core, new ModuleData { Name = "Item" }, ModuleLayoutType.Detail, string.Empty);
            return (module, (WebApiField)module.GetField("Api")!, host);
        }

        static Task<WebApiExecuteResponse?> Respond(int statusCode, string body = "", string error = "")
            => Task.FromResult<WebApiExecuteResponse?>(new WebApiExecuteResponse { StatusCode = statusCode, Body = body, ErrorMessage = error });

        static JsonField Json(Module module, string name) => (JsonField)module.GetField(name)!;
        static TextField Text(Module module, string name) => (TextField)module.GetField(name)!;
        static NumberField Number(Module module, string name) => (NumberField)module.GetField(name)!;
        static ListField List(Module module, string name) => (ListField)module.GetField(name)!;

        //キーの順序・空白に依存しない比較 (キーの大文字小文字は区別する)
        static void AssertSameJson(string? actual, string expected)
            => Assert.That(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(actual!), System.Text.Json.Nodes.JsonNode.Parse(expected)), Is.True, $"{actual} <> {expected}");

        [Test]
        public async Task Executeは引数のJSONとモジュール名とフィールド名だけを送り結果を返す()
        {
            var (_, api, host) = await CreateAsync();
            var args = new JsonObject { ["id"] = 3m, ["status"] = "open" };

            var ret = await api.ExecuteAsync(args);

            var request = host.Requests.Single();
            Assert.That(request.ModuleName, Is.EqualTo("Item"));
            Assert.That(request.FieldName, Is.EqualTo("Api"));
            //キーの先頭 1 文字は既定で小文字に揃えて送る
            AssertSameJson(request.Arguments, """{"id":3,"status":"open"}""");

            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.IsSuccess, Is.True);
            Assert.That(ret.ErrorMessage, Is.Empty);
            Assert.That(ret.JsonObject["name"], Is.EqualTo("abc"));
            //スクリプトの呼び出しは通知を出さない
            Assert.That(host.UI.Errors, Is.Empty);
            Assert.That(host.UI.Successes, Is.Empty);
        }

        [Test]
        public async Task 引数なしのExecuteは空のオブジェクトを送る()
        {
            var (_, api, host) = await CreateAsync();
            await api.ExecuteAsync();
            Assert.That(host.Requests.Single().Arguments, Is.EqualTo("{}"));
        }

        [Test]
        public async Task 応答の配列はJsonObjectの並びになりJSONでない応答は空()
        {
            var (_, api, host) = await CreateAsync();
            host.OnExecute = _ => Respond(200, """[{"name":"a"},{"name":"b"}]""");
            var ret = await api.ExecuteAsync();
            Assert.That(ret.JsonObject.Count, Is.EqualTo(2));
            Assert.That(ret.JsonObject.Cast<JsonObject>().Select(e => e["name"]), Is.EqualTo(new[] { "a", "b" }));

            foreach (var body in new[] { "123", "\"text\"", "<html></html>", "" })
            {
                host.OnExecute = _ => Respond(200, body);
                ret = await api.ExecuteAsync();
                Assert.That(ret.StatusCode, Is.EqualTo(200), body);
                Assert.That(ret.JsonObject.Count, Is.EqualTo(0), body);
            }
        }

        [Test]
        public async Task 相手の4xxと呼び出せなかったときは戻り値で分かる()
        {
            var (_, api, host) = await CreateAsync();
            host.OnExecute = _ => Respond(404, """{"error":"not found"}""");
            var ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(404));
            Assert.That(ret.IsSuccess, Is.False);
            Assert.That(ret.JsonObject["error"], Is.EqualTo("not found"));

            host.OnExecute = _ => Respond(0, error: "接続先 'Crm' に接続できませんでした。");
            ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.IsSuccess, Is.False);
            Assert.That(ret.ErrorMessage, Is.EqualTo("接続先 'Crm' に接続できませんでした。"));

            //ホストが例外 / 届かなかった (null)
            host.OnExecute = _ => throw new InvalidOperationException("failed");
            ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.EqualTo("failed"));
            host.OnExecute = _ => Task.FromResult<WebApiExecuteResponse?>(null);
            ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(0));
            Assert.That(ret.ErrorMessage, Is.Not.Empty);
            Assert.That(host.UI.Errors, Is.Empty);
        }

        [Test]
        public async Task ボタンは成功なら通知しない()
        {
            var (_, api, host) = await CreateAsync();
            await api.ClickAsync();
            Assert.That(host.Requests.Single().Arguments, Is.EqualTo("{}"));
            Assert.That(host.UI.Errors, Is.Empty);
            Assert.That(host.UI.Successes, Is.Empty);
        }

        [Test]
        public async Task ボタンは失敗ならエラーを通知する()
        {
            var (_, api, host) = await CreateAsync();
            host.OnExecute = _ => Respond(0, error: "接続先 'Crm' が設定にありません。");
            await api.ClickAsync();
            Assert.That(host.UI.Errors.Single(), Is.EqualTo("接続先 'Crm' が設定にありません。"));

            //相手の 4xx / 5xx は状態コードの文言
            host.OnExecute = _ => Respond(500, "{}");
            await api.ClickAsync();
            Assert.That(host.UI.Errors[1], Is.EqualTo("Web API の呼び出しに失敗しました (HTTP 500)。"));
        }

        [Test]
        public async Task ボタンは入力チェックをせず引数なしで送る()
        {
            //必須の Name が空でも送る (画面の内容は引数にしない)
            var (_, api, host) = await CreateAsync(nameRequired: true);
            await api.ClickAsync();
            Assert.That(host.Requests.Single().Arguments, Is.EqualTo("{}"));
            Assert.That(host.UI.Errors, Is.Empty);
        }

        [Test]
        public async Task ボタンで呼んだ後にOnCompletedを結果を付けて呼ぶ()
        {
            const string script = """
                void Done(WebApiResult result)
                {
                    if (result.IsSuccess)
                    {
                        Result.Value = "ok";
                    }
                    else
                    {
                        Result.Value = "ng";
                    }
                }
                """;
            var (module, api, host) = await CreateAsync(onCompleted: "Done", script: script);
            var result = (TextField)module.GetField("Result")!;

            await api.ClickAsync();
            Assert.That(result.Value, Is.EqualTo("ok"));

            host.OnExecute = _ => Respond(500, "{}");
            await api.ClickAsync();
            Assert.That(result.Value, Is.EqualTo("ng"));

            //スクリプトの Execute では呼ばない
            await result.SetValueAsync("x");
            await api.ExecuteAsync();
            Assert.That(result.Value, Is.EqualTo("x"));
        }

        [Test]
        public async Task 実行中は二重に頼まない()
        {
            var (_, api, host) = await CreateAsync();
            var gate = new TaskCompletionSource<WebApiExecuteResponse?>();
            host.OnExecute = _ => gate.Task;

            var first = api.ExecuteAsync();
            Assert.That(api.IsRunning, Is.True);

            var second = await api.ExecuteAsync();
            Assert.That(second.StatusCode, Is.EqualTo(0));
            Assert.That(second.ErrorMessage, Is.Not.Empty);
            await api.ClickAsync();
            Assert.That(host.Requests, Has.Count.EqualTo(1));

            gate.SetResult(new WebApiExecuteResponse { StatusCode = 200 });
            Assert.That((await first).StatusCode, Is.EqualTo(200));
            Assert.That(api.IsRunning, Is.False);
        }

        [Test]
        public async Task 書けない状態や無効では頼まない()
        {
            var (_, api, host) = await CreateAsync();
            api.IsViewOnly = true;
            Assert.That((await api.ExecuteAsync()).StatusCode, Is.EqualTo(0));
            await api.ClickAsync();
            api.IsViewOnly = false;
            api.IsEnabled = false;
            Assert.That((await api.ExecuteAsync()).StatusCode, Is.EqualTo(0));
            await api.ClickAsync();

            Assert.That(host.Requests, Is.Empty);
            Assert.That(host.UI.Errors, Is.Empty);
        }

        // ===== キーの名前 (Execute(args) だけ揃える) =====

        [Test]
        public async Task Executeの引数はキーの先頭を入れ子まで揃えて送る()
        {
            var (_, api, host) = await CreateAsync();
            var args = new JsonObject { ["userName"] = "山田", ["Address"] = new JsonObject { ["zipCode"] = "100", ["City"] = "x" } };
            await api.ExecuteAsync(args);
            AssertSameJson(host.Requests.Single().Arguments, """{"userName":"山田","address":{"zipCode":"100","city":"x"}}""");
        }

        [Test]
        public async Task 先頭大文字の指定ならExecuteの引数のキーの先頭を大文字に揃える()
        {
            var (_, api, host) = await CreateAsync(upperCamelCaseNames: true);
            var args = new JsonObject { ["userName"] = "山田", ["address"] = new JsonObject { ["zipCode"] = "100" } };
            await api.ExecuteAsync(args);
            AssertSameJson(host.Requests.Single().Arguments, """{"UserName":"山田","Address":{"ZipCode":"100"}}""");
        }

        [Test]
        public void キーの揃え方は入れ子と配列の中まで_2文字目以降と値はそのまま_同じ名前は後勝ち()
        {
            var json = """{"Id":1,"UserName":"山田","user_name":"a","Address":{"ZipCode":"100","City":{"Name":"x"}},"Items":[{"ItemCode":"A","Qty":2},[{"Deep":true}],3,"Str"],"Note":null}""";
            Assert.That(WebApiField.ConvertNames(json, false), Is.EqualTo(
                """{"id":1,"userName":"山田","user_name":"a","address":{"zipCode":"100","city":{"name":"x"}},"items":[{"itemCode":"A","qty":2},[{"deep":true}],3,"Str"],"note":null}"""));
            Assert.That(WebApiField.ConvertNames("""{"id":1,"userName":"山田","user_name":"a","items":[{"itemCode":"A"},[{"deep":true}]]}""", true), Is.EqualTo(
                """{"Id":1,"UserName":"山田","User_name":"a","Items":[{"ItemCode":"A"},[{"Deep":true}]]}"""));
            Assert.That(WebApiField.ConvertNames("""{"Id":1,"id":2,"name":"a"}""", false), Is.EqualTo("""{"id":2,"name":"a"}"""));
        }

        // ===== ArgumentsField =====

        [Test]
        public async Task 引数なしのExecuteはArgumentsFieldのJsonFieldの今の内容をキーそのままで送る()
        {
            var (module, api, host) = await CreateAsync(argumentsField: "Args");
            await Json(module, "Args").SetJsonAsync("""{"title":"A","qty":1,"Extra":{"Code":"X"},"Items":[{"No":1}]}""");
            //ノードを画面で編集した後の値が送られる。結線していないキーも残る。先頭 1 文字は揃えない
            await Text(module, "ArgTitle").SetValueAsync("B");
            await Number(module, "ArgQty").SetValueAsync(5m);

            await api.ExecuteAsync();
            AssertSameJson(host.Requests.Single().Arguments, """{"title":"B","qty":5,"Extra":{"Code":"X"},"Items":[{"No":1}]}""");
        }

        [Test]
        public async Task ボタンもArgumentsFieldのJsonFieldの今の内容をキーそのままで送る()
        {
            var (module, api, host) = await CreateAsync(argumentsField: "Args", upperCamelCaseNames: true);
            await Json(module, "Args").SetJsonAsync("""{"title":"A","extra":"keep"}""");
            await Text(module, "ArgTitle").SetValueAsync("B");

            await api.ClickAsync();
            AssertSameJson(host.Requests.Single().Arguments, """{"title":"B","extra":"keep"}""");
        }

        [Test]
        public async Task ArgumentsFieldのJsonFieldが値なしなら空のオブジェクトを送る()
        {
            var (_, api, host) = await CreateAsync(argumentsField: "Args");
            await api.ExecuteAsync();
            Assert.That(host.Requests.Single().Arguments, Is.EqualTo("{}"));
        }

        [TestCase("Nope")]
        [TestCase("Name")]
        public async Task ArgumentsFieldが無いかJsonFieldでなければ空のオブジェクトを送る(string argumentsField)
        {
            var (_, api, host) = await CreateAsync(argumentsField: argumentsField);
            await api.ExecuteAsync();
            await api.ClickAsync();
            Assert.That(host.Requests.Select(e => e.Arguments), Is.EqualTo(new[] { "{}", "{}" }));
        }

        [Test]
        public async Task ArgumentsFieldがあっても引数を渡したExecuteは渡した引数を送る()
        {
            var (module, api, host) = await CreateAsync(argumentsField: "Args");
            await Json(module, "Args").SetJsonAsync("""{"title":"A"}""");
            await api.ExecuteAsync(new JsonObject { ["Code"] = "C1" });
            AssertSameJson(host.Requests.Single().Arguments, """{"code":"C1"}""");
        }

        // ===== ResultField =====

        [Test]
        public async Task 成功したオブジェクトの応答はResultFieldのJsonFieldに入りノードのフィールドに値が出る()
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            const string body = """{"name":"abc","age":30,"Other":{"x":1}}""";
            host.OnExecute = _ => Respond(200, body);

            var ret = await api.ExecuteAsync();
            Assert.That(ret.ErrorMessage, Is.Empty);
            Assert.That(ret.Body, Is.EqualTo(body));
            Assert.That(Json(module, "Res").Value, Is.EqualTo(body));
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("abc"));
            Assert.That(Number(module, "ResAge").Value, Is.EqualTo(30m));
            //結線していないキーも JsonField の値に残る (GetJson で読める)
            AssertSameJson(Json(module, "Res").GetJson(), body);
        }

        [Test]
        public async Task ボタンと引数を渡したExecuteでもResultFieldに入る()
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            host.OnExecute = _ => Respond(200, """{"name":"a"}""");
            await api.ClickAsync();
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("a"));

            host.OnExecute = _ => Respond(200, """{"name":"b"}""");
            await api.ExecuteAsync(new JsonObject { ["id"] = 1m });
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("b"));
            Assert.That(host.UI.Errors, Is.Empty);
        }

        [TestCase(200, "")]
        [TestCase(200, " \r\n ")]
        [TestCase(404, """{"name":"not found"}""")]
        [TestCase(500, """{"name":"error"}""")]
        public async Task 空の応答と失敗ではResultFieldは変わらずErrorMessageも入らない(int statusCode, string body)
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            const string before = """{"name":"keep","age":1}""";
            await Json(module, "Res").SetJsonAsync(before);
            host.OnExecute = _ => Respond(statusCode, body);

            var ret = await api.ExecuteAsync();
            Assert.That(ret.Body, Is.EqualTo(body));
            Assert.That(Json(module, "Res").Value, Is.EqualTo(before));
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("keep"));
            Assert.That(Number(module, "ResAge").Value, Is.EqualTo(1m));
            Assert.That(ret.ErrorMessage, Is.Empty);
        }

        //普通の JsonField (外側はオブジェクト) に配列・値だけ・JSON でない本文: JsonField が受け付けない
        [TestCase("""[{"name":"a"}]""")]
        [TestCase("123")]
        [TestCase("\"text\"")]
        [TestCase("<html></html>")]
        public async Task JsonFieldの形に合わない応答とJSONでない応答ではResultFieldは変わらずErrorMessageが入る(string body)
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            const string before = """{"name":"keep","age":1}""";
            await Json(module, "Res").SetJsonAsync(before);
            host.OnExecute = _ => Respond(200, body);

            var ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.Body, Is.EqualTo(body));
            Assert.That(ret.ErrorMessage, Is.Not.Empty);
            Assert.That(Json(module, "Res").Value, Is.EqualTo(before));
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("keep"));
            Assert.That(Number(module, "ResAge").Value, Is.EqualTo(1m));
            //スクリプトの Execute は通知しない
            Assert.That(host.UI.Errors, Is.Empty);

            //ボタンは成功の状態コードでも ErrorMessage があればエラーを通知する
            await api.ClickAsync();
            Assert.That(host.UI.Errors.Single(), Is.EqualTo(ret.ErrorMessage));
            Assert.That(Json(module, "Res").Value, Is.EqualTo(before));
        }

        [Test]
        public async Task 配列の応答はUseNodeValueAsJsonのJsonFieldに入り一覧に行が出る()
        {
            var (module, api, host) = await CreateAsync(resultField: "Users");
            const string body = """[{"name":"a","age":1,"email":"a@x"},{"name":"b","age":2}]""";
            host.OnExecute = _ => Respond(200, body);

            await api.ClickAsync();
            Assert.That(host.UI.Errors, Is.Empty);
            var rows = List(module, "UserRows").Rows;
            Assert.That(rows.Select(e => ((TextField)e.GetField("RowName")!).Value), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(rows.Select(e => ((NumberField)e.GetField("RowAge")!).Value), Is.EqualTo(new decimal?[] { 1m, 2m }));
            //結線していないキー (email) も JsonField の JSON に残る
            AssertSameJson(Json(module, "Users").GetJson(), body);

            //Execute() / Execute(args) でも同じ。次の応答で行が入れ替わる
            host.OnExecute = _ => Respond(200, """[{"name":"c","age":3}]""");
            var ret = await api.ExecuteAsync(new JsonObject { ["id"] = 1m });
            Assert.That(ret.ErrorMessage, Is.Empty);
            Assert.That(List(module, "UserRows").Rows.Select(e => ((TextField)e.GetField("RowName")!).Value), Is.EqualTo(new[] { "c" }));
        }

        [Test]
        public async Task オブジェクトの中の配列の応答は普通のJsonFieldに入り一覧に行が出る()
        {
            var (module, api, host) = await CreateAsync(resultField: "Wrapped");
            const string body = """{"total":2,"items":[{"name":"a","age":1},{"name":"b","age":2}]}""";
            host.OnExecute = _ => Respond(200, body);

            var ret = await api.ExecuteAsync();
            Assert.That(ret.ErrorMessage, Is.Empty);
            Assert.That(List(module, "WrappedRows").Rows.Select(e => ((TextField)e.GetField("RowName")!).Value), Is.EqualTo(new[] { "a", "b" }));
            AssertSameJson(Json(module, "Wrapped").GetJson(), body);
        }

        [TestCase("""{"items":[{"name":"a"}]}""")]
        [TestCase("\"text\"")]
        [TestCase("""[{"name":1}]""")]
        [TestCase("not json")]
        public async Task UseNodeValueAsJsonのJsonFieldに形や型が合わない応答では変わらずErrorMessageが入る(string body)
        {
            var (module, api, host) = await CreateAsync(resultField: "Users");
            const string before = """[{"name":"keep","age":1}]""";
            await Json(module, "Users").SetJsonAsync(before);
            host.OnExecute = _ => Respond(200, body);

            await api.ClickAsync();
            Assert.That(host.UI.Errors, Has.Count.EqualTo(1));
            Assert.That(Json(module, "Users").Value, Is.EqualTo(before));
            Assert.That(List(module, "UserRows").Rows.Select(e => ((TextField)e.GetField("RowName")!).Value), Is.EqualTo(new[] { "keep" }));
        }

        [Test]
        public async Task ArgumentsFieldが配列のJsonFieldなら画面で編集した後の配列をそのまま送る()
        {
            var (module, api, host) = await CreateAsync(argumentsField: "Users", upperCamelCaseNames: true);
            await Json(module, "Users").SetJsonAsync("""[{"name":"a","age":1,"Extra":"keep"},{"name":"b","age":2}]""");
            //行を画面で編集する
            var rows = List(module, "UserRows").Rows;
            await ((TextField)rows[0].GetField("RowName")!).SetValueAsync("A2");
            await ((NumberField)rows[1].GetField("RowAge")!).SetValueAsync(20m);

            await api.ClickAsync();
            //キーは変えない (UseUpperCamelCaseNames は掛からない)。行の結線していないキーも残る
            AssertSameJson(host.Requests.Single().Arguments, """[{"name":"A2","age":1,"Extra":"keep"},{"name":"b","age":20}]""");
        }

        [Test]
        public async Task Executeに配列のJsonObjectを渡すと配列のまま中のオブジェクトのキーの先頭を揃えて送る()
        {
            var (_, api, host) = await CreateAsync();
            var args = new JsonObject();
            args.Add(new JsonObject { ["Name"] = "a", ["Detail"] = new JsonObject { ["ZipCode"] = "1" } });
            args.Add(new JsonObject { ["Name"] = "b" });
            await api.ExecuteAsync(args);
            AssertSameJson(host.Requests.Single().Arguments, """[{"name":"a","detail":{"zipCode":"1"}},{"name":"b"}]""");
        }

        [Test]
        public async Task Executeに空のJsonObjectを渡すと空のオブジェクトとして送る()
        {
            var (_, api, host) = await CreateAsync();
            await api.ExecuteAsync(new JsonObject());
            AssertSameJson(host.Requests.Single().Arguments, "{}");
        }

        [Test]
        public async Task 呼び出せなかったときもResultFieldは変わらずBodyは空()
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            await Json(module, "Res").SetJsonAsync("""{"name":"keep"}""");
            host.OnExecute = _ => Respond(0, error: "接続できませんでした。");
            var ret = await api.ExecuteAsync();
            Assert.That(ret.Body, Is.Empty);
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("keep"));
        }

        [Test]
        public async Task 型が合わない応答ではResultFieldは変わらずErrorMessageが入る()
        {
            var (module, api, host) = await CreateAsync(resultField: "Res");
            const string before = """{"name":"keep","age":1}""";
            await Json(module, "Res").SetJsonAsync(before);
            //name は Text のノード
            const string body = """{"name":123,"age":2}""";
            host.OnExecute = _ => Respond(200, body);

            var ret = await api.ExecuteAsync();
            Assert.That(ret.StatusCode, Is.EqualTo(200));
            Assert.That(ret.Body, Is.EqualTo(body));
            Assert.That(ret.JsonObject["age"], Is.EqualTo(2m));
            Assert.That(ret.ErrorMessage, Is.Not.Empty);
            Assert.That(Json(module, "Res").Value, Is.EqualTo(before));
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("keep"));
            Assert.That(Number(module, "ResAge").Value, Is.EqualTo(1m));
            //スクリプトの Execute は通知しない
            Assert.That(host.UI.Errors, Is.Empty);

            //ボタンは成功の状態コードでも ErrorMessage があればエラーを通知する
            await api.ClickAsync();
            Assert.That(host.UI.Errors.Single(), Is.EqualTo(ret.ErrorMessage));
            Assert.That(Text(module, "ResName").Value, Is.EqualTo("keep"));
        }

        [TestCase("Nope")]
        [TestCase("Name")]
        public async Task ResultFieldが無いかJsonFieldでなければ何もしない(string resultField)
        {
            var (module, api, host) = await CreateAsync(resultField: resultField);
            var ret = await api.ExecuteAsync();
            Assert.That(ret.IsSuccess, Is.True);
            Assert.That(ret.ErrorMessage, Is.Empty);
            Assert.That(Text(module, "Name").Value, Is.Null);
            Assert.That(Json(module, "Res").Value, Is.Null);
        }

        [Test]
        public async Task ボタンはResultFieldに入れた後にOnCompletedを呼ぶ()
        {
            const string script = """
                void Done(WebApiResult result)
                {
                    Result.Value = ResName.Value;
                }
                """;
            var (module, api, host) = await CreateAsync(onCompleted: "Done", script: script, resultField: "Res");
            host.OnExecute = _ => Respond(200, """{"name":"from api"}""");
            await api.ClickAsync();
            Assert.That(Text(module, "Result").Value, Is.EqualTo("from api"));
        }

        [Test]
        public async Task 保存には何も載せない()
        {
            var (_, api, _) = await CreateAsync();
            Assert.That(api.IsModified, Is.False);
            Assert.That(api.GetData(), Is.Null);
        }
    }
}
