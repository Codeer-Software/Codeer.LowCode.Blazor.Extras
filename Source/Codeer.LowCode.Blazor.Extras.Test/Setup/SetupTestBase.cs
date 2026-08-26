using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using System.IO.Compression;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Test.Setup
{
    /// <summary>
    /// セットアップサービステストの共通処理。
    /// 一時フォルダに最小のデザインプロジェクトを組み立て、生成後は実際の読込経路
    /// (DesignDataFileManager) で読み直して検証する (JSON の崩れは「静かに既定値へ落ちる」ため)。
    /// </summary>
    public abstract class SetupTestBase
    {
        protected string ProjectDir = string.Empty;

        protected static string ExampleDesignDir
            => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "Example", "Design"));

        [SetUp]
        public void SetUpProjectDir()
        {
            ProjectDir = Path.Combine(Path.GetTempPath(), $"clb_setup_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(ProjectDir, "Modules"));
            Directory.CreateDirectory(Path.Combine(ProjectDir, "PageFrames"));
        }

        [TearDown]
        public void TearDownProjectDir()
        {
            try { Directory.Delete(ProjectDir, true); } catch { }
        }

        /// <summary>最小プロジェクトを組み立てる (app.clprj + ユーザーモジュール + 申請書 + PageFrame)。</summary>
        protected void CreateFixture(string userModuleName = "AppUser",
            string userNameField = "Name", string userEmailField = "Email")
        {
            WriteFile("app.clprj", $$"""
                {
                  "CurrentUserModuleDesignName": "{{userModuleName}}"
                }
                """);

            if (userModuleName == "AppUser")
            {
                File.Copy(Path.Combine(ExampleDesignDir, "Modules", "AppUser.mod.json"),
                    Path.Combine(ProjectDir, "Modules", "AppUser.mod.json"));
            }
            else
            {
                var user = new ModuleDesign
                {
                    Name = userModuleName,
                    DataSourceName = "Main",
                    DbTable = "app_users",
                };
                user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
                user.Fields.Add(new TextFieldDesign { Name = userNameField, DbColumn = "name" });
                user.Fields.Add(new TextFieldDesign { Name = userEmailField, DbColumn = "email" });
                SaveModule(user);
            }

            var request = new ModuleDesign
            {
                Name = "Request",
                DataSourceName = "Main",
                DbTable = "requests",
            };
            request.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            request.Fields.Add(new TextFieldDesign { Name = "Title", DbColumn = "title" });
            SaveModule(request);

            WriteFile(Path.Combine("PageFrames", "Main.frm.json"),
                JsonConverterEx.SerializeObject(new PageFrameDesign { Name = "Main", IsApplicationRoot = true }));
        }

        protected void SaveModule(ModuleDesign module)
            => WriteFile(Path.Combine("Modules", $"{module.Name}.mod.json"), JsonConverterEx.SerializeObject(module));

        protected void WriteFile(string relativePath, string content)
            => File.WriteAllText(Path.Combine(ProjectDir, relativePath), content, new UTF8Encoding(true));

        /// <summary>実際の読込経路でプロジェクトを読む (App.zip 形式のため一時 zip を経由)。</summary>
        protected DesignData Load()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"clb_setup_zip_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.CreateFromDirectory(ProjectDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        protected string ReadModuleJson(string moduleName)
            => File.ReadAllText(Path.Combine(ProjectDir, "Modules", $"{moduleName}.mod.json"));

        protected bool ModuleFileExists(string moduleName)
            => File.Exists(Path.Combine(ProjectDir, "Modules", $"{moduleName}.mod.json"));
    }
}
