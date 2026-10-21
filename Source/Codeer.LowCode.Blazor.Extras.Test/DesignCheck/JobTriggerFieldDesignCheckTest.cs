using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Repository;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class JobTriggerFieldDesignCheckTest
    {
        [Test]
        public void 指摘なし()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            var field = new JobTriggerFieldDesign { Name = "Job1", JobName = "Sample" };
            mod.Fields.Add(field);
            Assert.That(field.CheckDesign(new DesignCheckContext(mod.Name, designData, new())), Is.Empty);
        }

        [Test]
        public void ジョブ名は必須()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            var field = new JobTriggerFieldDesign { Name = "Job1", JobName = " " };
            mod.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("JobTriggerFieldDesign:1"));
            Assert.That(ret[0].Message, Is.EqualTo("ジョブ名を設定してください。"));
            ret[0].AssertFieldLocation(mod.Name, "Job1", "JobName");
        }

        [Test]
        public void モジュールの行の条件はジョブの実行可否に効かないので指摘()
        {
            var (designData, mod) = Utilities.CreateDesignData();
            mod.Fields.Add(new TextFieldDesign { Name = "State" });
            mod.DataWriteCondition.ModuleName = mod.Name;
            mod.DataWriteCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Draft") };
            var field = new JobTriggerFieldDesign { Name = "Job1", JobName = "Sample" };
            mod.Fields.Add(field);
            var ret = field.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("JobTriggerFieldDesign:2"));
            Assert.That(ret[0].Message, Is.EqualTo("読み取り/書き込みできる行の条件 (DataReadCondition / DataWriteCondition) は、JobTriggerField 'Job1' のジョブの実行可否には適用されません (画面ではその行でボタンが押せなくなるだけです)。行の状態で止めるときは、ジョブが受け取った内容や DB を見て確かめてください。"));
            ret[0].AssertFieldLocation(mod.Name, "Job1", "JobName");
        }

        [Test]
        public void 子の無い複合条件や値の無い比較は条件なし扱い()
        {
            //本体が保存前に刈る条件 (子の無い複合・値が未設定の比較・変数が空の比較) は無いものとして扱う
            var (designData, mod) = Utilities.CreateDesignData();
            mod.Fields.Add(new TextFieldDesign { Name = "State" });
            mod.DataReadCondition.ModuleName = mod.Name;
            mod.DataReadCondition.Condition = new MultiMatchCondition
            {
                Children =
                {
                    new MultiMatchCondition(),
                    //値が必須の比較 (NonNull) で値が無いものは刈られる。FieldValueMatchCondition の NullValue は「null との比較」で実条件
                    new FieldValueMatchConditionNonNull { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = new NullValue() },
                    new FieldVariableMatchCondition { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Variable = string.Empty },
                }
            };
            var field = new JobTriggerFieldDesign { Name = "Job1", JobName = "Sample" };
            mod.Fields.Add(field);
            Assert.That(field.CheckDesign(new DesignCheckContext(mod.Name, designData, new())), Is.Empty);
        }

        static (DesignData designData, ModuleDesign mod, PermissionFieldDesign permission, JobTriggerFieldDesign job) BuildWithPermission(bool excludeMode)
        {
            var (designData, mod) = Utilities.CreateDesignData();
            designData.AppSettings.CurrentUserModuleDesignName = "user";
            var user = Utilities.CreateModule("user");
            user.Fields.Add(new BooleanFieldDesign { Name = "IsAdmin" });
            designData.AddModule(user);
            mod.Fields.Add(new TextFieldDesign { Name = "State" });
            var job = new JobTriggerFieldDesign { Name = "Job", JobName = "Sample" };
            mod.Fields.Add(job);
            //除外モードは「State 以外 = Job」が対象
            var permission = excludeMode
                ? new PermissionFieldDesign { Name = "Permission1", IsExcludeMode = true, TargetFields = { "State" } }
                : new PermissionFieldDesign { Name = "Permission1", TargetFields = { "Job" } };
            mod.Fields.Add(permission);
            return (designData, mod, permission, job);
        }

        [Test]
        public void このフィールドを対象にしたPermissionFieldのCurrentUser条件は指摘なし()
        {
            var (designData, mod, permission, job) = BuildWithPermission(excludeMode: false);
            permission.WriteCondition.ModuleName = mod.Name;
            permission.WriteCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "CurrentUser.IsAdmin.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(true) };
            Assert.That(job.CheckDesign(new DesignCheckContext(mod.Name, designData, new())), Is.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void このフィールドを対象にしたPermissionFieldの行条件は指摘(bool excludeMode)
        {
            //行データを参照する条件は画面でボタンが押せなくなるだけで、サーバーのジョブの実行可否には使われない (除外モードで巻き込まれたときも)
            var (designData, mod, permission, job) = BuildWithPermission(excludeMode);
            permission.ReadCondition.ModuleName = mod.Name;
            permission.ReadCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Start") };
            var ret = job.CheckDesign(new DesignCheckContext(mod.Name, designData, new()));
            Assert.That(ret, Has.Count.EqualTo(1));
            Assert.That(ret[0].Code, Is.EqualTo("JobTriggerFieldDesign:3"));
            Assert.That(ret[0].Message, Is.EqualTo("PermissionField 'Permission1' の行データを参照する条件は、JobTriggerField 'Job' のジョブの実行可否には適用されません (画面ではその行でボタンが押せなくなるだけです)。CurrentUser を参照する条件を使用してください。"));
            ret[0].AssertFieldLocation(mod.Name, "Job", "JobName");
        }

        [Test]
        public void 対象でないPermissionFieldの行条件は指摘しない()
        {
            var (designData, mod, permission, job) = BuildWithPermission(excludeMode: false);
            permission.TargetFields.Clear();
            permission.TargetFields.Add("State");
            permission.WriteCondition.ModuleName = mod.Name;
            permission.WriteCondition.Condition = new FieldValueMatchCondition
            { SearchTargetVariable = "State.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create("Start") };
            Assert.That(job.CheckDesign(new DesignCheckContext(mod.Name, designData, new())), Is.Empty);
        }

        [Test]
        public void Json往復と既定値()
        {
            var field = new JobTriggerFieldDesign
            {
                Name = "Job1",
                JobName = "Sample",
                ShowButton = false,
                ButtonText = "取込",
                UseSubmitData = false,
                IsAsynchronous = true,
            };
            var json = JsonConverterEx.SerializeObject(field);
            Assert.That(json, Does.Contain("\"TypeFullName\": \"Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign\""));

            var back = (JobTriggerFieldDesign)JsonConverterEx.DeserializeObject<FieldDesignBase>(json)!;
            Assert.That(back.Name, Is.EqualTo("Job1"));
            Assert.That(back.JobName, Is.EqualTo("Sample"));
            Assert.That(back.ShowButton, Is.False);
            Assert.That(back.ButtonText, Is.EqualTo("取込"));
            Assert.That(back.UseSubmitData, Is.False);
            Assert.That(back.IsAsynchronous, Is.True);

            //既定値: ボタンを出す・画面の内容を渡す・同期
            var empty = (JobTriggerFieldDesign)JsonConverterEx.DeserializeObject<FieldDesignBase>(
                "{\"Name\":\"Job2\",\"TypeFullName\":\"Codeer.LowCode.Blazor.Extras.Designs.JobTriggerFieldDesign\"}")!;
            Assert.That(empty.ShowButton, Is.True);
            Assert.That(empty.UseSubmitData, Is.True);
            Assert.That(empty.IsAsynchronous, Is.False);
            Assert.That(empty.CreateData(), Is.Null);
        }
    }
}
