using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designer.Setup;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    //セットアップ (承認 / 編集履歴 / 監査ログ) がユーザーモジュールの表示名フィールドを決める既定。
    //Empty テンプレートの AppUser には "Name" が無く、ログインアカウント契約の DisplayName の役割 (日本語名) だけが表示名を指す
    public class UserModuleFieldsTest
    {
        static DesignData Design(ModuleDesign user)
        {
            var design = new DesignData();
            design.AppSettings.CurrentUserModuleDesignName = user.Name;
            design.AddModule(user);
            return design;
        }

        [Test]
        public void Nameがあればそれ()
        {
            var user = new ModuleDesign { Name = "AppUser", Fields = { new TextFieldDesign { Name = "Name" }, new LoginAccountContractFieldDesign { Name = "LoginAccount", DisplayName = "表示名" } } };
            Assert.That(UserModuleFields.DefaultDisplayNameField(Design(user)), Is.EqualTo("Name"));
        }

        [Test]
        public void Nameが無ければログインアカウント契約の表示名の役割()
        {
            var user = new ModuleDesign { Name = "AppUser", Fields = { new TextFieldDesign { Name = "ユーザー識別名" }, new TextFieldDesign { Name = "表示名" }, new LoginAccountContractFieldDesign { Name = "LoginAccount", LoginName = "ユーザー識別名", DisplayName = "表示名" } } };
            Assert.That(UserModuleFields.DefaultDisplayNameField(Design(user)), Is.EqualTo("表示名"));
            Assert.That(UserModuleFields.DefaultDisplayNameField(Design(user), "AppUser"), Is.EqualTo("表示名"));
        }

        [Test]
        public void どちらも無い_モジュールが無いときはName()
        {
            var user = new ModuleDesign { Name = "AppUser", Fields = { new TextFieldDesign { Name = "Login" } } };
            Assert.That(UserModuleFields.DefaultDisplayNameField(Design(user)), Is.EqualTo("Name"));
            Assert.That(UserModuleFields.DefaultDisplayNameField(new DesignData(), "Nope"), Is.EqualTo("Name"));
        }
    }
}
