using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.SystemSettings;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// セットアップの headless CLI verb (CCFD = Claude Code からも同じ生成を呼べる)。
    ///
    /// approval-setup:
    ///   &lt;designer.exe&gt; approval-setup "&lt;projectDir&gt;" [--target &lt;module&gt;] [--field Approval] [--db-column approval_id]
    ///     [--prefix &lt;P&gt;] [--data-source &lt;name&gt;] [--user-module AppUser] [--user-name-field Name] [--user-email-field Email]
    ///     [--route standard|simple|none] [--no-turn-mail] [--no-pageframe] [--ddl-out "&lt;path.sql&gt;"]
    ///
    /// mail-history-setup:
    ///   &lt;designer.exe&gt; mail-history-setup "&lt;projectDir&gt;" [--name MailHistory] [--data-source &lt;name&gt;]
    ///     [--user-module AppUser] [--no-pageframe] [--ddl-out "&lt;path.sql&gt;"]
    ///
    /// DDL は実行しない (--ddl-out へ書き出し、適用は sql verb またはユーザーが行う)。
    /// 終了コード: 0 = 成功 / 2 = 失敗。
    /// </summary>
    internal static class SetupCli
    {
        internal const string ApprovalVerb = "approval-setup";
        internal const string MailHistoryVerb = "mail-history-setup";

        internal static void Register()
        {
            HeadlessCliVerbs.Register(ApprovalVerb, RunApproval);
            HeadlessCliVerbs.Register(MailHistoryVerb, RunMailHistory);
        }

        static int RunApproval(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine($"usage: {ApprovalVerb} \"<projectDir>\" [--target <module>] [--prefix <P>] ...");
                return 2;
            }
            var projectDir = Path.GetFullPath(args[1]);
            var named = ParseNamed(args);

            var designData = LoadDesignData(projectDir);
            var (dataSourceName, dataSourceType) = ResolveDataSource(projectDir, named.GetValueOrDefault("--data-source"));

            var options = new ApprovalSetupOptions
            {
                DataSourceName = dataSourceName,
                TargetModuleName = named.GetValueOrDefault("--target", string.Empty),
                FieldName = named.GetValueOrDefault("--field", "Approval"),
                DbColumn = named.GetValueOrDefault("--db-column", "approval_id"),
                Prefix = named.GetValueOrDefault("--prefix", string.Empty),
                UserModuleName = named.GetValueOrDefault("--user-module",
                    string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                        ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName),
                UserDisplayNameField = named.GetValueOrDefault("--user-name-field", "Name"),
                UserEmailField = named.GetValueOrDefault("--user-email-field", "Email"),
                RouteMaster = named.GetValueOrDefault("--route", "standard").ToLowerInvariant() switch
                {
                    "none" => ApprovalRouteMasterKind.None,
                    "simple" => ApprovalRouteMasterKind.Simple,
                    _ => ApprovalRouteMasterKind.Standard,
                },
                UseTurnNotifyMail = !args.Contains("--no-turn-mail"),
                AddPageFrameLinks = !args.Contains("--no-pageframe"),
            };

            var result = ApprovalFlowSetupService.Run(designData, projectDir, options, dataSourceType);
            return Report(result, named.GetValueOrDefault("--ddl-out"));
        }

        static int RunMailHistory(string[] args)
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine($"usage: {MailHistoryVerb} \"<projectDir>\" [--name MailHistory] ...");
                return 2;
            }
            var projectDir = Path.GetFullPath(args[1]);
            var named = ParseNamed(args);

            var designData = LoadDesignData(projectDir);
            var (dataSourceName, dataSourceType) = ResolveDataSource(projectDir, named.GetValueOrDefault("--data-source"));

            var options = new MailHistorySetupOptions
            {
                ModuleName = named.GetValueOrDefault("--name", "MailHistory"),
                DataSourceName = dataSourceName,
                UserModuleName = named.GetValueOrDefault("--user-module",
                    string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                        ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName),
                AddPageFrameLink = !args.Contains("--no-pageframe"),
            };

            var result = MailHistorySetupService.Run(designData, projectDir, options, dataSourceType);
            return Report(result, named.GetValueOrDefault("--ddl-out"));
        }

        static Dictionary<string, string> ParseNamed(string[] args)
        {
            var named = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 2; i < args.Length - 1; i++)
            {
                if (args[i].StartsWith("--", StringComparison.Ordinal) && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    named[args[i]] = args[i + 1];
            }
            return named;
        }

        //GetDesignData は App.zip を読む形式のため、プロジェクトフォルダを一時 zip にして読む
        static DesignData LoadDesignData(string projectDir)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"clb_setup_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.CreateFromDirectory(projectDir, Path.Combine(tempDir, "App.zip"));
                return DesignDataFileManager.GetDesignData(tempDir, new DesignData());
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        //designer.settings.json の DataSources から名前と DB 種別を解決する (未指定は先頭)
        static (string Name, DataSourceType Type) ResolveDataSource(string projectDir, string? name)
        {
            var path = Path.Combine(projectDir, "designer.settings.json");
            if (!File.Exists(path)) return (name ?? string.Empty, DataSourceType.SQLite);

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("DataSources", out var sources) || sources.GetArrayLength() == 0)
                return (name ?? string.Empty, DataSourceType.SQLite);

            foreach (var source in sources.EnumerateArray())
            {
                var sourceName = source.GetProperty("Name").GetString() ?? string.Empty;
                if (name != null && sourceName != name) continue;

                var type = source.TryGetProperty("DataSourceType", out var t)
                    && Enum.TryParse<DataSourceType>(t.GetString(), out var parsed)
                        ? parsed : DataSourceType.SQLite;
                return (sourceName, type);
            }
            throw new InvalidOperationException($"DataSource not found: {name}");
        }

        static int Report(SetupResult result, string? ddlOutPath)
        {
            var report = new StringBuilder();
            report.AppendLine($"created: {string.Join(", ", result.CreatedModules)}");
            report.AppendLine($"skipped (existing): {string.Join(", ", result.SkippedModules)}");
            report.AppendLine($"parent wired: {result.ParentWired}");
            foreach (var note in result.Notes) report.AppendLine($"note: {note}");

            if (result.Ddl.Count > 0)
            {
                var ddl = string.Join(Environment.NewLine, result.Ddl);
                if (!string.IsNullOrEmpty(ddlOutPath))
                {
                    File.WriteAllText(ddlOutPath, ddl, new UTF8Encoding(true));
                    report.AppendLine($"ddl: {ddlOutPath} (実行してテーブルを作成してください)");
                }
                else
                {
                    report.AppendLine("ddl (実行してテーブルを作成してください):");
                    report.AppendLine(ddl);
                }
            }

            Console.WriteLine(report.ToString());
            return 0;
        }
    }
}
