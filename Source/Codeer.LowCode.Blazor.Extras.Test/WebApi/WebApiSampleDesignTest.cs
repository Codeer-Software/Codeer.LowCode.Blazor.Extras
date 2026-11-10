using Codeer.LowCode.Blazor.Components.Dialog;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
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

namespace Codeer.LowCode.Blazor.Extras.Test.WebApi
{
    /// <summary>Example の WebApiSample (WebApiField のサンプル画面) が読み込めてデザインチェックを通り、スクリプトが動くこと (ホストは偽物)。</summary>
    [NonParallelizable]
    public class WebApiSampleDesignTest
    {
        static string DesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Example", "Design"));

        //GetDesignData は App.zip を読む形式のため、リポジトリのデザインフォルダを一時 zip にして読む
        static DesignData Load()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"webapi_design_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                System.IO.Compression.ZipFile.CreateFromDirectory(DesignDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        class NullServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType) => null;
        }

        class NullUIService : IUIService
        {
            public Task NotifySuccess(string message) => Task.CompletedTask;
            public Task NotifyError(string message) => Task.CompletedTask;
            public Task CloseDialog(Module moduleData, string button) => Task.CompletedTask;
            public Task ClosePanel(Module module, string button) => Task.CompletedTask;
            public Task ClosePopup(Module module, string button) => Task.CompletedTask;
            public Task<string> ShowDialog(Module moduleData, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPopup(Module moduleData, int x, int y, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<string> ShowPanel(Module moduleData, DialogButton[] buttons, PanelAlignment alignment) => Task.FromResult(string.Empty);
            public Task<string> ShowMessageBox(string title, string message, DialogButton[] buttons) => Task.FromResult(string.Empty);
            public Task<bool> DownloadFile(MemoryStream stream, string name) => Task.FromResult(true);
        }

        Func<ExtrasRequest, Task<ExtrasResponse?>>? _savedExtras;

        [SetUp]
        public void SetUp() => _savedExtras = ExtrasTransport.Handler;

        [TearDown]
        public void TearDown() => ExtrasTransport.Handler = _savedExtras;

        [Test]
        public async Task サンプルのWebApiSampleが読み込めてデザインチェックを通りスクリプトが動く()
        {
            var d = Load();
            var module = d.Modules.Find("WebApiSample");
            Assert.That(module, Is.Not.Null);
            //配列で返る API の例の一覧の行モジュール
            var userRow = d.Modules.Find("WebApiSampleUser");
            Assert.That(userRow, Is.Not.Null);
            var noTables = new Dictionary<string, List<Codeer.LowCode.Blazor.DataIO.Db.Definition.DbTableDefinition>>();
            foreach (var target in new[] { module!, userRow! })
            {
                foreach (var e in target.Fields)
                {
                    Assert.That(e.CheckDesign(new DesignCheckContext(target.Name, d, noTables)), Is.Empty, $"{target.Name}.{e.Name}");
                }
            }

            var requests = new List<WebApiExecuteRequest>();
            ExtrasTransport.Handler = envelope =>
            {
                Assert.That(envelope.Feature, Is.EqualTo(WebApiNames.Feature));
                Assert.That(envelope.Action, Is.EqualTo(WebApiNames.Execute));
                var request = JsonConverterEx.DeserializeObject<WebApiExecuteRequest>(envelope.Payload)!;
                requests.Add(request);
                //users は配列で返る
                var body = request.FieldName == "UsersApi"
                    ? """[{"id":1,"name":"Leanne Graham","email":"a@example.com","address":{"city":"x"}},{"id":2,"name":"Ervin Howell","email":"b@example.com"}]"""
                    : """{"id":1,"name":"Leanne Graham"}""";
                var response = new WebApiExecuteResponse { StatusCode = 200, Body = body };
                return Task.FromResult<ExtrasResponse?>(new ExtrasResponse { Payload = JsonConverterEx.SerializeObject(response, indented: false) });
            };

            //サンプルのモジュールとスクリプトだけで動かす (他のモジュールの型の登録は要らない)
            var only = new DesignData();
            only.AddModule(module);
            only.AddModule(userRow);
            only.Scripts["WebApiSample"] = d.Scripts["WebApiSample"];
            var app = new HarnessAppService(only);
            app.GetScriptRuntimeTypeManager().AddType<WebApiResult>();
            app.GetScriptRuntimeTypeManager().AddService(new Toaster(null!));
            var core = new Codeer.LowCode.Blazor.RequestInterfaces.Services(new NullServiceProvider(), app, app, new DummyNavigationService(), new NullUIService(), new ListLogger());
            var instance = await ModuleCreationService.CreateModuleAsync(core, new ModuleData { Name = "WebApiSample" }, ModuleLayoutType.Detail, string.Empty);

            //取得ボタン (スクリプトで Execute)
            await ((TextField)instance.GetField("UserId")!).SetValueAsync("1");
            await instance.ExecuteScriptAsync("Search_OnClick");
            Assert.That(requests.Single().FieldName, Is.EqualTo("UserApi"));
            using (var json = JsonDocument.Parse(requests.Single().Arguments))
            {
                //Execute(args) はキーの先頭を小文字に揃えて送る
                Assert.That(json.RootElement.GetProperty("id").GetString(), Is.EqualTo("1"));
            }
            Assert.That(((TextField)instance.GetField("UserName")!).Value, Is.EqualTo("Leanne Graham"));

            //投稿ボタン (WebApiField のボタン + OnCompleted)
            await ((WebApiField)instance.GetField("PostApi")!).ClickAsync();
            Assert.That(requests.Last().FieldName, Is.EqualTo("PostApi"));
            Assert.That(((TextField)instance.GetField("Result")!).Value, Is.EqualTo("投稿しました"));

            //スクリプトなしの取得 (引数は JsonField UserQuery、結果は JsonField UserJson に入りノードのフィールドに出る)
            await ((TextField)instance.GetField("QueryId")!).SetValueAsync("1");
            await ((WebApiField)instance.GetField("UserJsonApi")!).ClickAsync();
            Assert.That(requests.Last().FieldName, Is.EqualTo("UserJsonApi"));
            Assert.That(requests.Last().Arguments, Is.EqualTo("""{"id":"1"}"""));
            Assert.That(((TextField)instance.GetField("JsonUserName")!).Value, Is.EqualTo("Leanne Graham"));

            //配列で返る API (結果は UseNodeValueAsJson の JsonField UserList に入り、一覧 UserListRows に行が出る)
            var usersApi = (WebApiField)instance.GetField("UsersApi")!;
            await usersApi.ClickAsync();
            Assert.That(requests.Last().FieldName, Is.EqualTo("UsersApi"));
            Assert.That(requests.Last().Arguments, Is.EqualTo("{}"));
            var rows = ((ListField)instance.GetField("UserListRows")!).Rows;
            Assert.That(rows.Select(e => ((NumberField)e.GetField("UserNo")!).Value), Is.EqualTo(new decimal?[] { 1m, 2m }));
            Assert.That(rows.Select(e => ((TextField)e.GetField("UserName")!).Value), Is.EqualTo(new[] { "Leanne Graham", "Ervin Howell" }));
            Assert.That(rows.Select(e => ((TextField)e.GetField("UserEmail")!).Value), Is.EqualTo(new[] { "a@example.com", "b@example.com" }));
        }
    }
}
