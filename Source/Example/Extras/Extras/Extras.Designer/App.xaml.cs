using System.Configuration;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Windows;
using Codeer.LowCode.Blazor.Components.AppParts.Loading;
using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Extensibility.Views;
using Codeer.LowCode.Blazor.Designer.Extra;
using Codeer.LowCode.Blazor.Designer.Models;
using Codeer.LowCode.Blazor.Designer.Views.Windows;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.SystemSettings;
using Extras.Client.Shared.AITextAnalyzer;
using Extras.Client.Shared.ScriptObjects;
using Extras.Designer.Lib;
using Extras.Designer.Lib.AI;
using Extras.Designer.Lib.DbTableToModule;
using Extras.Designer.Lib.ExcelToModule;
using Extras.Designer.Lib.ModuleToClass;
using Extras.Designer.Lib.SeleniumPageObject;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Extras.Designer
{
    public partial class App : DesignerApp
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            typeof(CalendarField).ToString();

            AISettings.Instance.OpenAIEndPoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_ENDPOINT") ?? string.Empty;
            AISettings.Instance.OpenAIKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") ?? string.Empty;
            AISettings.Instance.ChatModel = "gpt-4o";

            Codeer.LowCode.Blazor.License.LicenseManager.IsAutoUpdate =
                bool.TryParse(ConfigurationManager.AppSettings["IsLicenseAutoUpdate"], out var val) ? val : true;

            Services.AddSingleton<IDbAccessorFactory, DbAccessorFactory>();
            Services.AddSingleton<IAITextAnalyzerCore, AITextAnalyzerCoreDummy>();
            ScriptRuntimeTypeManager.AddType(typeof(ExcelCellIndex));
            ScriptRuntimeTypeManager.AddType(typeof(Extras.Client.Shared.ScriptObjects.Excel));
            ScriptRuntimeTypeManager.AddService(new Toaster(null!));
            ScriptRuntimeTypeManager.AddService(new WebApiService(null!, null!));
            ScriptRuntimeTypeManager.AddType<WebApiResult>();
            ScriptRuntimeTypeManager.AddService(new MailService());
            ScriptRuntimeTypeManager.AddService(new LoadingService());
            ScriptRuntimeTypeManager.AddType<LoadingService.LoadingScope>();

            BlazorRuntime.InstallBundleCss("Extras.Client.Shared");
            BlazorRuntime.InstallBundleCss("Codeer.LowCode.Blazor.Extras");

            IconCandidate.Icons.AddRange(Extras.Designer.Properties.Resources.bootstrap_icons
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).Order());

            DesignerTemplateCandidate.Templates.Add(new DesignerTemplate
            {
                Create = CreateGettingStandard,
                Name = "GettingStarted",
                Description =
                    "The sample project reads, writes, and deletes data in the \r\n\"C:\\Codeer.LowCode.Blazor.Local\"; folder. \r\n;Please do not place any data in this folder that would be problematic if overwritten or deleted. You can change this folder later.",
            });
            DesignerTemplateCandidate.Templates.Add(new DesignerTemplate
            {
                Create = CreateEmpty,
                Name = "Empty",
                Description = "Empty template.",
            });

            base.OnStartup(e);

            MainWindow.Title = "Extras";
            DesignerEnvironment.AddMainMenu(ImportModulesFromExcel, "Tools", "Import Module from Excel");
            DesignerEnvironment.AddMainMenu(ImportModulesFromDdTables, "Tools", "Import Modules from Database");
            DesignerEnvironment.AddMainMenu(ExportPageObject, "Tools", "Export PageObject");

            DesignerEnvironment.AddSolutionExplorerMenu(CreateDDL, SolutionExplorerMenuTarget.Module, "Create DDL");
            DesignerEnvironment.AddSolutionExplorerMenu(CreateFieldDataClass, SolutionExplorerMenuTarget.Module, "Create FieldData Class");
            DesignerEnvironment.AddSolutionExplorerMenu(CreateEfClass, SolutionExplorerMenuTarget.Module, "Create EF Class");

            if (!string.IsNullOrEmpty(AISettings.Instance.OpenAIKey))
            {
                QuerySettingPropertyControl.CreateQueryChat = dataSource => new QueryChat(DesignerEnvironment, AISettings.Instance, dataSource);
                DesignerEnvironment.AddMainMenu(CreateModulesByAI, "Tools", "Create Modules by AI");
                DesignerEnvironment.AddSolutionExplorerMenu(e => CreateDBInformation(e, true), SolutionExplorerMenuTarget.Module, "Create DB Name (AI)", "All");
                DesignerEnvironment.AddSolutionExplorerMenu(e => CreateDBInformation(e, false), SolutionExplorerMenuTarget.Module, "Create DB Name (AI)", "Empty Only");
            }

            DesignerEnvironment.AddDbColumnTransformHandler(DbColumnTransformHandler);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender,
            System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"An unhandled exception occurred:  {e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        FieldDesignBase? DbColumnTransformHandler(DataSource dataSource, DbTableDefinition table, string columnName)
        {
            var col = table.Columns.FirstOrDefault(e => e.Name == columnName);
            if (col == null) return null;
            if (dataSource.DataSourceType == DataSourceType.PostgreSQL && col.Name == "xmin")
                return new OptimisticLockingFieldDesign { Name = SystemFieldNames.OptimisticLocking, DbColumn = columnName };
            return null;
        }

        private void ImportModulesFromDdTables()
        {
            if (string.IsNullOrEmpty(DesignerEnvironment.CurrentFileDirectory)) return;

            //ユーザの選択をゲットする
            var datasourceToTables = DesignerEnvironment.GetDesignerSettings().DataSources.ToDictionary(e => e.Name, e => DesignerEnvironment.GetDbInfo(e.Name));
            var userSelected = DbTableSelectWindow.ShowDialog(datasourceToTables);
            if (userSelected == null) return;

            //モジュールに変換
            var err = DbTableParser.Import(DesignerEnvironment, userSelected.Value.selectedDataSource, userSelected.Value.selectedTables);
            if (!string.IsNullOrEmpty(err)) DesignerEnvironment.ShowToast(err, false);
        }

        private void CreateDBInformation(SolutionExplorerMenuClickEventArgs e, bool isAll)
        {
            var dlg = new WaitingWindow();
            dlg.Loaded += async (_, _) =>
            {
                var modName = e.Item.Split(".").First();
                var mod = DesignerEnvironment.GetDesignData().Modules.Find(modName);
                if (mod == null)
                {
                    DesignerEnvironment.ShowToast("Module not found", false);
                    return;
                }
                var settingsFile = File.ReadAllText(Path.Combine(DesignerEnvironment.CurrentFileDirectory, "designer.settings.json"));
                var clone = mod.JsonClone();
                await DbNmaeCreator.CreateDbNames(DesignerEnvironment.GetDesignerSettings(), clone, isAll);

                File.WriteAllText(Path.Combine(DesignerEnvironment.CurrentFileDirectory, "Modules", e.Item), JsonConverterEx.SerializeObject(clone));
                dlg.Close();
            };
            dlg.ShowDialog();
        }

        private void CreateDDL(SolutionExplorerMenuClickEventArgs e)
        {
            var modName = e.Item.Split(".").First();
            var mod = DesignerEnvironment.GetDesignData().Modules.Find(modName);
            if (mod == null)
            {
                DesignerEnvironment.ShowToast("Module not found", false);
                return;
            }
            var config = DesignerEnvironment.GetDesignerSettings();
            var dataSource = config.DataSources.FirstOrDefault(e => e.Name == mod.DataSourceName);
            if (dataSource == null)
            {
                DesignerEnvironment.ShowToast("Invalid Data Source", false);
                return;
            }

            var ddl = mod.CreateDDL(dataSource.DataSourceType);

            new TextDisplayWindow
            {
                DisplayText = string.Join(Environment.NewLine, ddl),
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "DDL",
            }.Show();
        }

        private void CreateFieldDataClass(SolutionExplorerMenuClickEventArgs e)
        {
            var modName = e.Item.Split(".").First();
            var mod = DesignerEnvironment.GetDesignData().Modules.Find(modName);
            if (mod == null)
            {
                DesignerEnvironment.ShowToast("Module not found", false);
                return;
            }

            var classTxt = ClassGenerator.ModuleDesignToDataFieldClass(mod);

            new TextDisplayWindow
            {
                DisplayText = classTxt,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Module to Field Data Class",
            }.Show();
        }

        private void CreateEfClass(SolutionExplorerMenuClickEventArgs e)
        {
            var modName = e.Item.Split(".").First();
            var mod = DesignerEnvironment.GetDesignData().Modules.Find(modName);
            if (mod == null)
            {
                DesignerEnvironment.ShowToast("Module not found", false);
                return;
            }
            var config = DesignerEnvironment.GetDesignerSettings();
            var dataSource = config.DataSources.FirstOrDefault(e => e.Name == mod.DataSourceName);
            if (dataSource == null)
            {
                DesignerEnvironment.ShowToast("Invalid Data Source", false);
                return;
            }

            var classTxt = ClassGenerator.ModuleDesignToEfClass(mod, dataSource.DataSourceType);

            new TextDisplayWindow
            {
                DisplayText = classTxt,
                Owner = Application.Current.MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Title = "Module to EF Class",
            }.Show();
        }

        private class AITextAnalyzerCoreDummy : IAITextAnalyzerCore
        {
            public Task<ModuleData?> FileToModuleDataAsync(string moduleName, string fileName, StreamContent content)
                => throw new NotImplementedException();

            public Task<ModuleData?> TextToModuleDataAsync(string moduleName, string text)
                => throw new NotImplementedException();
        }

        private void ImportModulesFromExcel()
        {
            if (string.IsNullOrEmpty(DesignerEnvironment.CurrentFileDirectory))
            {
                return;
            }

            var dialog = new OpenFileDialog
            {
                Filter = "Excel files (*.xlsx)|*.xlsx",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var ddl = new ExcelImporter
                {
                    ProjectPath = DesignerEnvironment.CurrentFileDirectory
                }.Import(dialog.FileName);
                if (string.IsNullOrEmpty(ddl)) return;

                new TextDisplayWindow
                {
                    DisplayText = ddl,
                    Owner = MainWindow,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Title = "DDL",
                }.Show();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void CreateModulesByAI()
        {
            if (string.IsNullOrEmpty(DesignerEnvironment.CurrentFileDirectory)) return;
            var window = new AIChatWindow(new ModuleCreator(DesignerEnvironment, AISettings.Instance))
            {
                Owner = MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            window.Show();
        }

        private void ExportPageObject()
        {
            if (string.IsNullOrEmpty(DesignerEnvironment.CurrentFileDirectory))
            {
                return;
            }

            var nameInputDialog = new NameInputDialog
            {
                Owner = MainWindow,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            if (nameInputDialog.ShowDialog() != true)
            {
                return;
            }

            var ns = nameInputDialog.NameText;

            var folderDialog = new OpenFolderDialog();
            if (folderDialog.ShowDialog() != true)
            {
                return;
            }

            var target = folderDialog.FolderName;
            var designData = DesignerEnvironment.GetDesignData();
            new SeleniumPageObjectBuilder
            {
                TargetPath = target,
                Namespace = ns,
            }.Build(designData);

            DesignerEnvironment.ShowToast("PageObject exported", true);
        }

        static void CreateEmpty(string path)
        {
            using Stream stream = new MemoryStream(Extras.Designer.Properties.Resources.EmptyTemplate);
            ZipFile.ExtractToDirectory(stream, path);
        }

        static void CreateGettingStandard(string path)
        {
            using (Stream stream = new MemoryStream(Extras.Designer.Properties.Resources.GettingStartedTemplate))
            {
                ZipFile.ExtractToDirectory(stream, path);
            }

            var dbPath = "C:\\Codeer.LowCode.Blazor.Local\\Data\\sqlite_sample.db";
            if (!File.Exists(dbPath))
            {
                if (!File.Exists(dbPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
                    File.WriteAllBytes(dbPath, Extras.Designer.Properties.Resources.sqlite_sample);
                }
            }
        }
    }
}
