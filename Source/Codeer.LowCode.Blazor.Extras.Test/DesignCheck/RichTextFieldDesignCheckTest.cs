using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

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
    }
}
