using Codeer.LowCode.Blazor.DataIO.Db.Definition;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// メール機能のセットアップ。デザイン側でメールに必要なものを一度に整える:
    /// 1. (任意) 送信履歴モジュール (MailHistoryContractField 同梱・誰も書けない保護条件・一覧・ページリンク)
    ///    + (任意) 送信明細モジュール (1 宛先 1 行。解決後の件名・本文と成否)
    /// 2. サーバー設定 (appsettings の Mail / プロバイダセクション) の案内
    /// すべて冪等 (既に有るものは触らない)。DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)。
    /// 承認フローのセットアップも「メールを使う」を選ぶとこれを内部で呼ぶ。
    /// </summary>
    public static class MailSetupService
    {
        public static SetupResult Run(DesignData designData, string designDir, MailSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            if (options.CreateHistoryModule) CreateHistoryModule(designData, designDir, options, dataSourceType, existingTables, result);

            result.Notes.Add(CreateAppSettingsNote(options));
            return result;
        }

        static void CreateHistoryModule(DesignData designData, string designDir, MailSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables, SetupResult result)
        {
            var moduleName = options.HistoryModuleName;
            if (designData.Modules.Find(moduleName) != null
                || File.Exists(Path.Combine(designDir, "Modules", $"{moduleName}.mod.json")))
            {
                result.SkippedModules.Add(moduleName);
                return;
            }

            //保護条件 (誰も書けない) の判定に使うモジュールはデザインの CurrentUser モジュール
            var userModuleName = SetupUi.CurrentUserModuleName(designData);
            var detailName = options.CreateHistoryDetailModule ? options.HistoryDetailModuleName : string.Empty;
            if (!string.IsNullOrEmpty(detailName) &&
                (designData.Modules.Find(detailName) != null || File.Exists(Path.Combine(designDir, "Modules", $"{detailName}.mod.json"))))
            {
                result.SkippedModules.Add(detailName);
                result.Notes.Add($"{detailName} は既に存在するため、履歴モジュールは明細なしで生成しました。明細を使う場合は {moduleName} に一覧フィールドを置き、契約の「送信明細の一覧」に設定してください。");
                detailName = string.Empty;
            }

            var module = MailHistoryModuleFactory.Create(moduleName, options.DataSourceName, userModuleName, detailName);
            SaveModule(designDir, module);
            result.CreatedModules.Add(moduleName);
            result.Ddl.AddRange(module.CreateDDL(dataSourceType, existingTables));

            if (!string.IsNullOrEmpty(detailName))
            {
                var detail = MailHistoryModuleFactory.CreateDetail(detailName, moduleName, options.DataSourceName, userModuleName);
                SaveModule(designDir, detail);
                result.CreatedModules.Add(detailName);
                result.Ddl.AddRange(detail.CreateDDL(dataSourceType, existingTables));
                result.Notes.Add($"送信明細 ({detailName}) には宛先アドレスと解決後の本文が 1 宛先 1 行で残ります。{moduleName} / {detailName} の閲覧権限 (UserReadCondition) は管理者などに絞ってください。");
            }

            if (options.AddPageFrameLink)
            {
                ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir,
                    new List<(string, string, Action<PageLink>?)> { ("メール送信履歴", moduleName, null) }, result);
            }
        }

        /// <summary>サーバー設定 (appsettings.json) の案内。送信インフラの選択・設定はアプリ側 (デザインには持たない)。</summary>
        internal static string CreateAppSettingsNote(MailSetupOptions options)
        {
            var history = options.CreateHistoryModule
                ? $",\r\n    \"HistoryModuleName\": \"{options.HistoryModuleName}\""
                : string.Empty;
            return $$"""
                メール送信はサーバー設定で有効になります。サーバーの appsettings.json に次を設定してください:
                  "Mail": {
                    "DefaultInfraName": "<単発送信の既定インフラ (MailSenderTable の呼び名。例: Smtp / GraphApi / SendGrid / Gmail)>",
                    "DefaultBulkInfraName": "<一斉送信の既定インフラ (省略時は単発と同じ)>"{{history}}
                  }
                  使う送信インフラのセクション ("Smtp" / "GraphApi" / "SendGrid" / "Gmail" 等) をそれぞれ独立して書きます (使うものだけ)。
                  呼び名と設定の対応はアプリの MailSenderTable / Program.cs。秘密情報は環境変数やユーザーシークレットに置いてください。
                """;
        }

        static string UniqueFieldName(ModuleDesign module, string baseName)
        {
            var name = baseName;
            for (var i = 2; module.Fields.Any(e => e.Name == name); i++) name = baseName + i;
            return name;
        }

        //dotted リンク列はロード時にフィールドへ自動合成されるため、合成前の形で保存する (ApprovalFlowSetupService と同じ)
        static void SaveModule(string designDir, ModuleDesign module)
        {
            var toSave = module.JsonClone();
            toSave.Fields.RemoveAll(f => f.Name.Contains('.'));
            ApprovalFlowSetupService.SaveDesignFile(designDir, "Modules", $"{module.Name}.mod.json", JsonConverterEx.SerializeObject(toSave));
        }
    }
}
