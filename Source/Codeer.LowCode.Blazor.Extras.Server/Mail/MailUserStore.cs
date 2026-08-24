using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>「自分を差出人にする」で解決した操作ユーザーの情報。</summary>
    public class MailCurrentUser
    {
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// ユーザーモジュール (デザインの AppSettings.CurrentUserModuleDesignName = CurrentUser のモジュール) の検索。
    /// ①操作ユーザーの差出人情報 (「自分を差出人にする」= IsFromCurrentUser)、
    /// ②GmailApi ユーザー同意モードのユーザー単位トークン (差出人アドレス → GmailTokenField 列)。
    /// </summary>
    /// <remarks>
    /// 読み取りは**製品のデータ層 (ModuleDataIO のシステム内部経路)** を通す。生 SQL は書かない。
    /// トークン列は書き込み専用 (クライアントに返さない) なので、書き込み専用列も読める
    /// システム内部経路 (GetSystemRecordsAsync) をテンプレートが結線する。
    /// </remarks>
    public class MailUserStore
    {
        readonly DesignData _designData;
        readonly Func<SearchCondition, Task<List<ModuleData>>> _getSystemRecordsAsync;
        readonly Action<string> _logError;

        /// <param name="getSystemRecordsAsync">
        /// システムの記録用の読み取り (認可を通さず書き込み専用列も読む)。
        /// テンプレートの CustomizedModuleDataIO が ModuleDataIO.GetSystemRecordsAsync を公開して渡す。
        /// </param>
        public MailUserStore(DesignData designData,
            Func<SearchCondition, Task<List<ModuleData>>> getSystemRecordsAsync, Action<string> logError)
        {
            _designData = designData;
            _getSystemRecordsAsync = getSystemRecordsAsync;
            _logError = logError;
        }

        /// <summary>
        /// 操作ユーザー (認証コンテキストのユーザーId) の差出人情報を返す。
        /// 未設定・行なし・メール未登録は null (呼び出し側が失敗にする)。
        /// </summary>
        public async Task<MailCurrentUser?> FindCurrentUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            try
            {
                var module = FindUserModule();
                if (module == null) return null;
                var contract = FindContract(module);
                if (contract == null) return null;

                var selectFields = new List<string> { SystemFieldNames.Id, GetFieldPath(contract.Email) };
                if (!string.IsNullOrEmpty(contract.DisplayName)) selectFields.Add(GetFieldPath(contract.DisplayName));
                var row = (await _getSystemRecordsAsync(new SearchCondition
                {
                    ModuleName = module.Name,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = $"{SystemFieldNames.Id}.Value",
                        Comparison = MatchComparison.Equal,
                        Value = MultiTypeValue.Create(userId),
                    },
                    SelectFields = selectFields,
                })).FirstOrDefault();

                var email = GetText(row, contract.Email);
                if (string.IsNullOrEmpty(email)) return null;
                return new MailCurrentUser
                {
                    Email = email,
                    DisplayName = GetText(row, contract.DisplayName) ?? string.Empty,
                };
            }
            catch (Exception ex)
            {
                _logError($"Current user lookup failed for '{userId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 差出人アドレスのユーザートークンを復号して返す。未登録・設定不備は null (呼び出し側がシステムトークンにフォールバック)。
        /// 検索・復号の失敗はエラーログを出して null (送信自体は止めない)。
        /// </summary>
        /// <param name="encryptionKey">列の暗号化鍵 (Gmail 設定の TokenEncryptionKey)。</param>
        /// <remarks>
        /// トークン列は型 (GmailTokenField) で見つけるのでフィールド名の設定は要らない。
        /// ユーザーモジュールに GmailTokenField が無ければ「ユーザー単位トークンを使わない」= null。
        /// </remarks>
        public async Task<string?> FindRefreshTokenAsync(string mailAddress, string encryptionKey)
        {
            if (string.IsNullOrEmpty(mailAddress)) return null;
            try
            {
                var module = FindUserModule();
                if (module == null) return null;
                var tokenFields = module.Fields.OfType<GmailTokenFieldDesign>().ToList();
                if (tokenFields.Count == 0) return null;
                if (tokenFields.Count > 1)
                    _logError($"Module '{module.Name}' has {tokenFields.Count} GmailTokenFields. The first one ('{tokenFields[0].Name}') is used.");

                var contract = FindContract(module);
                if (contract == null) return null;

                var row = (await _getSystemRecordsAsync(new SearchCondition
                {
                    ModuleName = module.Name,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = contract.Email,
                        Comparison = MatchComparison.Equal,
                        Value = MultiTypeValue.Create(mailAddress),
                    },
                    SelectFields = new List<string> { SystemFieldNames.Id, tokenFields[0].Name },
                })).FirstOrDefault();

                var token = (row?.Fields.GetValueOrDefault(tokenFields[0].Name) as Data.GmailTokenFieldData)?.RefreshToken;
                if (string.IsNullOrEmpty(token)) return null;

                //列は暗号化して保存されている (GmailTokenHelper)。
                //暗号化されていない値は不正な経路で入ったものなので使わない (再登録してもらう)
                if (!GmailTokenProtector.IsProtected(token))
                {
                    _logError($"The stored Gmail token of '{mailAddress}' is not encrypted. Register it again from the user screen.");
                    return null;
                }
                return GmailTokenProtector.Unprotect(token, encryptionKey);
            }
            catch (Exception ex)
            {
                _logError($"User token lookup failed for '{mailAddress}': {ex.Message}");
                return null;
            }
        }

        //ユーザーモジュールはデザインの CurrentUser モジュール (認証ユーザーId で引ける唯一のモジュール)
        ModuleDesign? FindUserModule()
        {
            var moduleName = _designData.AppSettings.CurrentUserModuleDesignName;
            if (string.IsNullOrEmpty(moduleName)) return null;
            var module = _designData.Modules.Find(moduleName);
            if (module == null) _logError($"The current user module '{moduleName}' does not exist.");
            return module;
        }

        //アドレス・表示名は CurrentUser モジュールに置いた差出人契約 (MailSenderContractField) が宣言する
        Designs.MailSenderContractFieldDesign? FindContract(ModuleDesign module)
        {
            var contract = Extras.Mail.MailContracts.Sender(module);
            if (contract == null)
                _logError($"The current user module '{module.Name}' does not implement the mail sender contract. " +
                    "Put a MailSenderContractField on it and declare the mail address.");
            return contract;
        }

        //変数 ("Email.Value" / "Employee.Email.Value") のフィールド部分 (SelectFields とデータのキー)
        static string GetFieldPath(string variable) => new VariableName(variable).FieldName.FullName;

        static string? GetText(ModuleData? row, string? variable)
            => string.IsNullOrEmpty(variable)
                ? null
                : (row?.Fields.GetValueOrDefault(GetFieldPath(variable)) as ValueFieldDataBase<string>)?.Value;
    }
}
