using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    public class RichTextFieldDesignCheckTest
    {
        static List<DesignCheckInfo> Check(DesignData designData, ModuleDesign module, RichTextFieldDesign field)
        {
            module.Fields.Add(field);
            return field.CheckDesign(new DesignCheckContext("mod", designData, Utilities.CreateDataSource()));
        }

        static ModuleDesign AddUserModule(DesignData designData, bool withContract)
        {
            var user = new ModuleDesign { Name = "AppUser" };
            user.Fields.Add(new IdFieldDesign { Name = "Id" });
            user.Fields.Add(new TextFieldDesign { Name = "LoginName" });
            if (withContract) user.Fields.Add(new LoginAccountContractFieldDesign { Name = "Login", LoginName = "LoginName" });
            designData.AddModule(user);
            designData.AppSettings.CurrentUserModuleDesignName = "AppUser";
            return user;
        }

        [Test]
        public void メンションを使わなければユーザーのモジュールは要らない()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var ret = Check(designData, module, new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn" });
            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void メンションにはユーザーのモジュールが要る()
        {
            var (designData, module) = Utilities.CreateDesignData();
            var ret = Check(designData, module, new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true });
            Assert.That(ret.Select(e => e.Code), Is.EqualTo(new[] { DesignCheckCode.Create(typeof(RichTextFieldDesign), 1) }));
            ret[0].AssertFieldLocation("mod", "Body", nameof(RichTextFieldDesign.EnableMention));
        }

        [Test]
        public void メンションにはユーザーのモジュールのログインアカウント契約が要る()
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: false);
            var ret = Check(designData, module, new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true });
            Assert.That(ret.Select(e => e.Code), Is.EqualTo(new[] { DesignCheckCode.Create(typeof(RichTextFieldDesign), 2) }));
            ret[0].AssertFieldLocation("mod", "Body", nameof(RichTextFieldDesign.EnableMention));
            Assert.That(ret[0].Message, Does.Contain("AppUser"));
        }

        [Test]
        public void 契約があれば指摘なし()
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: true);
            var ret = Check(designData, module, new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true });
            Assert.That(ret, Is.Empty);
        }

        #region 候補の条件 (段階 2)

        static FieldValueMatchCondition Value(string variable) => new()
        {
            SearchTargetVariable = variable,
            Comparison = MatchComparison.Equal,
            Value = MultiTypeValue.Create("x"),
        };

        static FieldVariableMatchCondition Variable(string left, string right) => new()
        {
            SearchTargetVariable = left,
            Comparison = MatchComparison.Equal,
            Variable = right,
        };

        static List<DesignCheckInfo> CheckCondition(string moduleName, MatchConditionBase condition)
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: true);
            var field = new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true };
            field.MentionCandidateCondition.ModuleName = moduleName;
            field.MentionCandidateCondition.Condition = condition;
            return Check(designData, module, field);
        }

        [TestCase("AppUser")]
        [TestCase("")]
        public void 候補の条件_ユーザーのモジュールの項目とCurrentUserなら指摘なし(string moduleName)
        {
            var ret = CheckCondition(moduleName, MultiMatchCondition.And(
                Value("LoginName.Value"),
                Variable("LoginName.Value", "CurrentUser.LoginName.Value")));
            Assert.That(ret, Is.Empty);
        }

        [Test]
        public void 候補の条件_モジュールはユーザーのモジュール()
        {
            var ret = CheckCondition("mod", Value("LoginName.Value"));
            Assert.That(ret.Select(e => e.Code), Is.EqualTo(new[] { DesignCheckCode.Create(typeof(RichTextFieldDesign), 3) }));
            ret[0].AssertFieldLocation("mod", "Body", nameof(RichTextFieldDesign.MentionCandidateCondition));
            Assert.That(ret[0].Message, Does.Contain("AppUser"));
        }

        //左辺に無い項目 / 左辺に CurrentUser / 右辺の CurrentUser に無い項目 / 右辺が候補になる人の項目 (項目どうしの比較)
        [TestCase("Dept.Value", "")]
        [TestCase("CurrentUser.LoginName.Value", "")]
        [TestCase("LoginName.Value", "CurrentUser.Dept.Value")]
        [TestCase("LoginName.Value", "Id.Value")]
        public void 候補の条件_項目はユーザーのモジュールの項目(string left, string right)
        {
            var ret = CheckCondition("AppUser", MultiMatchCondition.And(string.IsNullOrEmpty(right) ? Value(left) : Variable(left, right)));
            Assert.That(ret.Select(e => e.Code), Is.EqualTo(new[] { DesignCheckCode.Create(typeof(RichTextFieldDesign), 4) }));
            ret[0].AssertFieldLocation("mod", "Body", nameof(RichTextFieldDesign.MentionCandidateCondition));
            Assert.That(ret[0].Message, Does.Contain(string.IsNullOrEmpty(right) ? left : right));
        }

        [Test]
        public void 候補の条件_メンションを使わなければ見ない()
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: true);
            var field = new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn" };
            field.MentionCandidateCondition.Condition = Value("Dept.Value");
            Assert.That(Check(designData, module, field), Is.Empty);
        }

        [Test]
        public void 候補の条件_ユーザーのモジュールの項目の名前の変更に追従する()
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: true);
            //このフィールドのモジュールにも同じ名前の項目がある (こちらの名前の変更には追従しない)
            module.Fields.Add(new TextFieldDesign { Name = "LoginName" });
            var field = new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true };
            var value = Value("LoginName.Value");
            var currentUser = Variable("LoginName.Value", "CurrentUser.LoginName.Value");
            field.MentionCandidateCondition.Condition = MultiMatchCondition.And(value, currentUser);
            module.Fields.Add(field);

            var other = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Field, ModuleName = module.Name, OwnerModule = module.Name, Source = "LoginName", Destination = "Account",
            });
            Assert.That(other.RenameNeeded, Is.False);

            var result = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Field, ModuleName = "AppUser", OwnerModule = module.Name, Source = "LoginName", Destination = "Account",
            });
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(value.SearchTargetVariable, Is.EqualTo("Account.Value"));
            Assert.That(currentUser.SearchTargetVariable, Is.EqualTo("Account.Value"));
            Assert.That(currentUser.Variable, Is.EqualTo("CurrentUser.Account.Value"));
        }

        [Test]
        public void 候補の条件_ユーザーのモジュールの名前の変更に追従する()
        {
            var (designData, module) = Utilities.CreateDesignData();
            AddUserModule(designData, withContract: true);
            var field = new RichTextFieldDesign { Name = "Body", DbColumn = "DbColumn", EnableMention = true };
            field.MentionCandidateCondition.ModuleName = "AppUser";
            field.MentionCandidateCondition.Condition = Value("LoginName.Value");
            module.Fields.Add(field);

            var result = field.ChangeName(new RenameContext(designData)
            {
                Type = RenameType.Module, ModuleName = "AppUser", OwnerModule = module.Name, Source = "AppUser", Destination = "Member",
            });
            Assert.That(result.RenameNeeded, Is.True);
            result.RenameAction();
            Assert.That(field.MentionCandidateCondition.ModuleName, Is.EqualTo("Member"));
        }

        #endregion
    }
}
