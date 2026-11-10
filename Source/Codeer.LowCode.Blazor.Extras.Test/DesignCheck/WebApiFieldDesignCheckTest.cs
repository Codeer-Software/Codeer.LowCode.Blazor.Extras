using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class WebApiFieldDesignCheckTest
    {
        static List<DesignCheckInfo> Check(WebApiFieldDesign field, Action<ModuleDesign>? arrange = null)
        {
            var (designData, mod) = Utilities.CreateDesignData();
            arrange?.Invoke(mod);
            mod.Fields.Add(field);
            return field.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
        }

        [TestCase("")]
        [TestCase("users")]
        [TestCase("users/{id}/orders")]
        [TestCase("/search?q={q}&page={page}")]
        public void 指摘なし(string path)
        {
            Assert.That(Check(new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm", Path = path }), Is.Empty);
        }

        [Test]
        public void 接続先は必須()
        {
            var ret = Check(new WebApiFieldDesign { Name = "Api", ConnectionName = " " });
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("WebApiFieldDesign:1"));
            Assert.That(ret[0].Message, Is.EqualTo("接続先を設定してください。"));
            ret[0].AssertFieldLocation("mod", "Api", "ConnectionName");
        }

        [TestCase("users/{id")]
        [TestCase("users/id}")]
        [TestCase("users/{}")]
        [TestCase("users/{ }")]
        [TestCase("users/{a{b}}")]
        public void パスの括弧が壊れていれば指摘(string path)
        {
            var ret = Check(new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm", Path = path });
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("WebApiFieldDesign:2"));
            Assert.That(ret[0].Message, Is.EqualTo($"パス '{path}' の書き方が正しくありません。差し込みは {{名前}} の形で、{{ と }} を対にしてください。"));
            ret[0].AssertFieldLocation("mod", "Api", "Path");
        }

        [Test]
        public void モジュールの行の条件は呼び出しの可否に効かないので指摘()
        {
            var ret = Check(new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm" }, mod =>
            {
                mod.Fields.Add(new TextFieldDesign { Name = "State" });
                mod.DataReadCondition.ModuleName = mod.Name;
                mod.DataReadCondition.Condition = new FieldValueMatchCondition
                { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Draft") };
            });
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("WebApiFieldDesign:3"));
            Assert.That(ret[0].Message, Is.EqualTo("読み取り/書き込みできる行の条件 (DataReadCondition / DataWriteCondition) は、WebApiField 'Api' の Web API の呼び出し可否には適用されません (画面ではその行でボタンが押せなくなるだけです)。"));
            ret[0].AssertFieldLocation("mod", "Api", "ConnectionName");
        }

        [Test]
        public void このフィールドを対象にしたPermissionFieldの行条件は指摘_CurrentUserの条件は指摘しない()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            designData.AppSettings.CurrentUserModuleDesignName = "user";
            var user = Utilities.CreateModule("user");
            user.Fields.Add(new BooleanFieldDesign { Name = "IsAdmin" });
            designData.AddModule(user);
            mod.Fields.Add(new TextFieldDesign { Name = "State" });
            var api = new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm" };
            mod.Fields.Add(api);
            var permission = new PermissionFieldDesign { Name = "Permission1", TargetFields = { "Api" } };
            mod.Fields.Add(permission);

            permission.WriteCondition.ModuleName = mod.Name;
            permission.WriteCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "CurrentUser.IsAdmin.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(true) };
            Assert.That(api.CheckDesign(new DesignCheckContext(mod.Name, designData, new())), Is.Empty);

            permission.WriteCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Start") };
            var ret = api.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("WebApiFieldDesign:4"));
            Assert.That(ret[0].Message, Is.EqualTo("PermissionField 'Permission1' の行データを参照する条件は、WebApiField 'Api' の Web API の呼び出し可否には適用されません (画面ではその行でボタンが押せなくなるだけです)。CurrentUser を参照する条件を使用してください。"));
        }

        [Test]
        public void 引数と結果のJsonFieldは同じモジュールのJsonFieldの名前なら指摘なし()
        {
            var ret = Check(new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm", ArgumentsField = "Args", ResultField = "Result" }, mod =>
            {
                mod.Fields.Add(new JsonFieldDesign { Name = "Args" });
                mod.Fields.Add(new JsonFieldDesign { Name = "Result" });
            });
            Assert.That(ret, Is.Empty);
        }

        [TestCase(nameof(WebApiFieldDesign.ArgumentsField), "Nope")]
        [TestCase(nameof(WebApiFieldDesign.ArgumentsField), "Text")]
        [TestCase(nameof(WebApiFieldDesign.ResultField), "Nope")]
        [TestCase(nameof(WebApiFieldDesign.ResultField), "Text")]
        public void 引数と結果のJsonFieldが無いかJsonFieldでなければ指摘(string member, string fieldName)
        {
            var field = new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm" };
            if (member == nameof(WebApiFieldDesign.ArgumentsField)) field.ArgumentsField = fieldName;
            else field.ResultField = fieldName;
            var ret = Check(field, mod => mod.Fields.Add(new TextFieldDesign { Name = "Text" }));
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("WebApiFieldDesign:5"));
            Assert.That(ret[0].Message, Is.EqualTo($"{member} の '{fieldName}' は同じモジュールの JsonField の名前ではありません。"));
            ret[0].AssertFieldLocation("mod", "Api", member);
        }

        [Test]
        public void 別のモジュールのJsonFieldの名前は指摘()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            var other = Utilities.CreateModule("other");
            other.Fields.Add(new JsonFieldDesign { Name = "Result" });
            designData.AddModule(other);
            var api = new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm", ResultField = "Result" };
            mod.Fields.Add(api);
            var ret = api.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
            Assert.That(ret.Single().Code, Is.EqualTo("WebApiFieldDesign:5"));
        }

        [Test]
        public void JsonFieldの名前の変更に追従する()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            mod.Fields.Add(new JsonFieldDesign { Name = "Args" });
            var api = new WebApiFieldDesign { Name = "Api", ConnectionName = "Crm", ArgumentsField = "Args", ResultField = "Args" };
            mod.Fields.Add(api);
            var context = new RenameContext(designData)
            {
                Type = RenameType.Field,
                ModuleName = mod.Name,
                OwnerModule = mod.Name,
                Source = "Args",
                Destination = "Params",
            };
            var result = api.ChangeName(context);
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(api.ArgumentsField, Is.EqualTo("Params"));
            Assert.That(api.ResultField, Is.EqualTo("Params"));
        }

        [Test]
        public void Json往復と既定値()
        {
            var field = new WebApiFieldDesign
            {
                Name = "Api",
                ConnectionName = "Crm",
                Method = WebApiMethod.Put,
                Path = "users/{id}",
                ShowButton = false,
                ButtonText = "更新",
                OnCompleted = "Done",
                ArgumentsField = "Args",
                ResultField = "Result",
            };
            var json = JsonConverterEx.SerializeObject(field);
            Assert.That(json, Does.Contain("\"TypeFullName\": \"Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign\""));
            Assert.That(json, Does.Contain("\"Method\": \"Put\""));

            var back = (WebApiFieldDesign)JsonConverterEx.DeserializeObject<FieldDesignBase>(json)!;
            Assert.That(back.ConnectionName, Is.EqualTo("Crm"));
            Assert.That(back.Method, Is.EqualTo(WebApiMethod.Put));
            Assert.That(back.Path, Is.EqualTo("users/{id}"));
            Assert.That(back.ShowButton, Is.False);
            Assert.That(back.ButtonText, Is.EqualTo("更新"));
            Assert.That(back.OnCompleted, Is.EqualTo("Done"));
            Assert.That(back.ArgumentsField, Is.EqualTo("Args"));
            Assert.That(back.ResultField, Is.EqualTo("Result"));

            var empty = (WebApiFieldDesign)JsonConverterEx.DeserializeObject<FieldDesignBase>(
                "{\"Name\":\"Api2\",\"TypeFullName\":\"Codeer.LowCode.Blazor.Extras.Designs.WebApiFieldDesign\"}")!;
            Assert.That(empty.Method, Is.EqualTo(WebApiMethod.Get));
            Assert.That(empty.ShowButton, Is.True);
            Assert.That(empty.ArgumentsField, Is.Empty);
            Assert.That(empty.ResultField, Is.Empty);
            Assert.That(empty.CreateData(), Is.Null);
        }
    }
}
