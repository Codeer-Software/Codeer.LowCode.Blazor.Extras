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
    /// 1. ユーザー (CurrentUser) モジュールの差出人契約 (MailSenderContractField) — 「自分を差出人にする」と Gmail ユーザートークン検索の前提
    /// 2. (任意) ユーザーモジュールの GmailTokenField — 本人の Gmail から送るためのトークン欄
    /// 3. (任意) 送信履歴モジュール (MailHistoryContractField 同梱・誰も書けない保護条件・一覧・ページリンク)
    /// 4. サーバー設定 (appsettings の Mail / プロバイダセクション) の案内
    /// すべて冪等 (既に有るものは触らない)。DDL は雛形として返す (実行は呼び出し側でユーザーの確認を挟む)。
    /// 承認フローのセットアップも「メールを使う」を選ぶとこれを内部で呼ぶ。
    /// </summary>
    public static class MailSetupService
    {
        /// <summary>差出人契約フィールドの既定名。</summary>
        internal const string SenderContractFieldName = "MailSender";

        /// <summary>Gmail トークンフィールドの既定名 / 列名。</summary>
        internal const string GmailTokenFieldName = "GmailToken";
        internal const string GmailTokenDbColumn = "gmail_token";

        public static SetupResult Run(DesignData designData, string designDir, MailSetupOptions options,
            DataSourceType dataSourceType, List<DbTableDefinition>? existingTables = null)
        {
            var result = new SetupResult();

            var userModule = designData.Modules.Find(options.UserModuleName);
            if (userModule == null)
            {
                result.Notes.Add($"ユーザーモジュール {options.UserModuleName} が見つからないため、差出人契約と Gmail トークン欄の追加をスキップしました。");
            }
            else
            {
                var userModified = false;
                if (options.AddSenderContract) userModified |= AddSenderContract(userModule, options, result);
                if (options.AddGmailTokenField) userModified |= AddGmailTokenField(userModule, dataSourceType, existingTables, result);
                if (userModified) SaveModule(designDir, userModule);
            }

            if (options.CreateHistoryModule) CreateHistoryModule(designData, designDir, options, dataSourceType, existingTables, result);

            result.Notes.Add(CreateAppSettingsNote(options));
            return result;
        }

        /// <summary>
        /// ユーザーモジュールに置かれた差出人契約から、メールアドレス・表示名のフィールド名を読む
        /// (自モジュールのフィールド参照 "Email.Value" のときだけ。リンクパスは対象外)。
        /// 承認フローのセットアップがユーザー項目の既定値に使う。
        /// </summary>
        internal static (string? EmailField, string? DisplayNameField) ReadSenderRoles(ModuleDesign? userModule)
        {
            var contract = userModule?.Fields.OfType<MailSenderContractFieldDesign>().FirstOrDefault();
            if (contract == null) return (null, null);
            return (ToOwnFieldName(contract.Email), ToOwnFieldName(contract.DisplayName));

            static string? ToOwnFieldName(string variable)
            {
                if (string.IsNullOrEmpty(variable) || !variable.EndsWith(".Value", StringComparison.Ordinal)) return null;
                var name = variable[..^".Value".Length];
                return name.Contains('.') ? null : name;
            }
        }

        //差出人契約: 無ければ追加。役割のフィールドがユーザーモジュールに無ければ追加せず案内する
        static bool AddSenderContract(ModuleDesign userModule, MailSetupOptions options, SetupResult result)
        {
            if (userModule.Fields.OfType<MailSenderContractFieldDesign>().Any())
            {
                result.Notes.Add($"{userModule.Name} には既に差出人契約 (MailSenderContractField) があります。");
                return false;
            }
            if (userModule.Fields.All(e => e.Name != options.UserEmailField))
            {
                result.Notes.Add($"{userModule.Name} にメールアドレスフィールド {options.UserEmailField} が無いため、差出人契約の追加をスキップしました。フィールドを作ってから再実行してください。");
                return false;
            }
            var hasDisplayName = userModule.Fields.Any(e => e.Name == options.UserDisplayNameField);
            if (!hasDisplayName)
                result.Notes.Add($"{userModule.Name} に表示名フィールド {options.UserDisplayNameField} が無いため、差出人の表示名は使わない設定にしました。");

            userModule.Fields.Add(new MailSenderContractFieldDesign
            {
                Name = UniqueFieldName(userModule, SenderContractFieldName),
                Email = $"{options.UserEmailField}.Value",
                DisplayName = hasDisplayName ? $"{options.UserDisplayNameField}.Value" : string.Empty,
            });
            result.Notes.Add($"{userModule.Name} に差出人契約 (MailSenderContractField) を追加しました。「自分を差出人にする」と Gmail ユーザートークン検索がこの宣言を使います。");
            return true;
        }

        //Gmail トークン欄: 無ければフィールド + 既定詳細レイアウトの末尾に配置 + 列追加 DDL
        static bool AddGmailTokenField(ModuleDesign userModule, DataSourceType dataSourceType,
            List<DbTableDefinition>? existingTables, SetupResult result)
        {
            if (userModule.Fields.OfType<GmailTokenFieldDesign>().Any())
            {
                result.Notes.Add($"{userModule.Name} には既に GmailTokenField があります。");
                return false;
            }
            var field = new GmailTokenFieldDesign
            {
                Name = UniqueFieldName(userModule, GmailTokenFieldName),
                DbColumnToken = GmailTokenDbColumn,
            };
            userModule.Fields.Add(field);

            if (userModule.DetailLayouts.TryGetValue(string.Empty, out var detail) && detail.Layout is GridLayoutDesign grid)
            {
                grid.Rows.Add(new GridRow
                {
                    Columns = { new GridColumn { Layout = new FieldLayoutDesign { FieldName = field.Name } } }
                });
            }
            else
            {
                result.Notes.Add($"{userModule.Name} の既定詳細レイアウトが Grid ではないため、{field.Name} の配置はスキップしました。レイアウトに手動で配置してください。");
            }
            result.Ddl.AddRange(SetupDbMapping.CreateAlterAddForField(userModule, field, dataSourceType, existingTables));
            result.Notes.Add($"{userModule.Name} に Gmail トークン欄 ({field.Name}) を追加しました。保存時の暗号化鍵 Gmail.TokenEncryptionKey をサーバー設定に用意してください。");
            return true;
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

            var module = MailHistoryModuleFactory.Create(moduleName, options.DataSourceName, options.UserModuleName);
            SaveModule(designDir, module);
            result.CreatedModules.Add(moduleName);
            result.Ddl.AddRange(module.CreateDDL(dataSourceType, existingTables));

            if (options.AddPageFrameLink)
            {
                ApprovalFlowSetupService.AddPageFrameLinks(designData, designDir,
                    new List<(string, string, Action<PageLink>?)> { ("メール送信履歴", moduleName, null) }, result);
            }
        }

        /// <summary>サーバー設定 (appsettings.json) の案内。Mail セクション + 既定インフラのセクション雛形。</summary>
        internal static string CreateAppSettingsNote(MailSetupOptions options)
        {
            var mail = new List<string> { $"    \"DefaultInfraName\": \"{options.DefaultInfraName}\"" };
            if (options.CreateHistoryModule) mail.Add($"    \"HistoryModuleName\": \"{options.HistoryModuleName}\"");

            var provider = options.DefaultInfraName switch
            {
                "GraphApi" => """
                    "GraphApi": {
                        "SenderMailAddress": "system@example.com",
                        "SenderDisplayName": "システム",
                        "TenantId": "",
                        "ClientId": "",
                        "ClientSecret": ""
                      }
                    """,
                "SendGrid" => """
                    "SendGrid": {
                        "SenderMailAddress": "system@example.com",
                        "SenderDisplayName": "システム",
                        "ApiKey": ""
                      }
                    """,
                "Gmail" => """
                    "Gmail": {
                        "SenderMailAddress": "system@example.com",
                        "SenderDisplayName": "システム",
                        "ClientSecret": "<client_secret.json のパスまたは JSON>",
                        "TokenSecret": "<token.json のパスまたは JSON>",
                        "TokenEncryptionKey": "<GmailTokenField を使う場合の暗号化鍵>"
                      }
                    """,
                _ => """
                    "Smtp": {
                        "SenderMailAddress": "system@example.com",
                        "SenderDisplayName": "システム",
                        "Host": "smtp.example.com",
                        "Port": "587",
                        "SSL": "true",
                        "UserName": "",
                        "Password": ""
                      }
                    """,
            };

            return $$"""
                メール送信はサーバー設定で有効になります。サーバーの appsettings.json に次を設定してください
                (送信インフラの呼び名は MailSenderTable の対応表。秘密情報は環境変数やユーザーシークレットに置くこと):
                  "Mail": {
                {{string.Join(",\r\n", mail)}}
                  },
                  {{provider.Trim()}}
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
