using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// メール送信履歴モジュールのセットアップ。モジュールを生成し、有効化に必要な
    /// サーバー設定 (appsettings の Mail.HistoryModuleName) のスニペットを案内する。
    /// </summary>
    public static class MailHistorySetupService
    {
        public static SetupResult Run(DesignData designData, string designDir, MailHistorySetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            if (designData.Modules.Find(options.ModuleName) != null
                || File.Exists(Path.Combine(designDir, "Modules", $"{options.ModuleName}.mod.json")))
            {
                result.SkippedModules.Add(options.ModuleName);
            }
            else
            {
                var module = MailHistoryModuleFactory.Create(options.ModuleName, options.DataSourceName, options.UserModuleName);
                ApprovalFlowSetupService.SaveDesignFile(designDir, "Modules",
                    $"{options.ModuleName}.mod.json", JsonConverterEx.SerializeObject(module));
                result.CreatedModules.Add(options.ModuleName);
                result.Ddl.AddRange(module.CreateDDL(dataSourceType, existingTables));

                if (options.AddPageFrameLink)
                {
                    ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir,
                        new List<(string, string)> { ("メール送信履歴", options.ModuleName) }, result);
                }
            }

            result.Notes.Add(CreateAppSettingsNote(options.ModuleName));
            return result;
        }

        /// <summary>履歴を有効化するサーバー設定 (appsettings.json) の案内。</summary>
        public static string CreateAppSettingsNote(string moduleName)
            => $$"""
                履歴の記録はサーバー設定で有効になります。サーバーの appsettings.json の Mail セクションに次を設定してください:
                "Mail": {
                  "HistoryModuleName": "{{moduleName}}"
                }
                """;
    }
}
