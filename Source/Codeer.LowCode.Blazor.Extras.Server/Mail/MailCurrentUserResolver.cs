using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
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
    /// 「自分を差出人にする」(IsFromCurrentUser) の操作ユーザー解決。すべての送信インフラで共通。
    /// 認証ユーザーId でデザインの CurrentUser モジュールを製品のデータ層 (操作ユーザーの認可付き) から読み、
    /// 差出人契約 (MailSenderContractField) が宣言するアドレス・表示名を返す。
    /// </summary>
    public class MailCurrentUserResolver
    {
        readonly DesignData _designData;
        readonly ModuleDataIO _io;
        readonly Action<string> _logError;

        public MailCurrentUserResolver(DesignData designData, ModuleDataIO io, Action<string> logError)
        {
            _designData = designData;
            _io = io;
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
                var module = MailUserModule.Find(_designData, _logError);
                if (module == null) return null;
                var contract = MailUserModule.FindContract(module, _logError);
                if (contract == null) return null;

                var selectFields = new List<string> { SystemFieldNames.Id, GetFieldPath(contract.Email) };
                if (!string.IsNullOrEmpty(contract.DisplayName)) selectFields.Add(GetFieldPath(contract.DisplayName));
                var row = (await _io.GetListAsync(new SearchCondition
                {
                    ModuleName = module.Name,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = $"{SystemFieldNames.Id}.Value",
                        Comparison = MatchComparison.Equal,
                        Value = MultiTypeValue.Create(userId),
                    },
                    SelectFields = selectFields,
                }, 0)).Items.FirstOrDefault();

                var email = GetText(row, contract.Email);
                if (string.IsNullOrEmpty(email)) return null;
                return new MailCurrentUser
                {
                    Email = email,
                    DisplayName = GetText(row, contract.DisplayName),
                };
            }
            catch (Exception ex)
            {
                _logError($"Current user lookup failed for '{userId}': {ex.Message}");
                return null;
            }
        }

        //変数 ("Email.Value" / "Employee.Email.Value") のフィールド部分 (SelectFields とデータのキー)
        static string GetFieldPath(string variable) => new VariableName(variable).FieldName.FullName;

        static string GetText(ModuleData? row, string variable)
            => string.IsNullOrEmpty(variable) ? string.Empty : ModuleDataValues.GetString(row, GetFieldPath(variable));
    }

    /// <summary>ユーザーモジュール (デザインの CurrentUser モジュール) と差出人契約の解決。メール機能の共通部品。</summary>
    internal static class MailUserModule
    {
        //ユーザーモジュールはデザインの CurrentUser モジュール (認証ユーザーId で引ける唯一のモジュール)
        internal static ModuleDesign? Find(DesignData designData, Action<string> logError)
        {
            var moduleName = designData.AppSettings.CurrentUserModuleDesignName;
            if (string.IsNullOrEmpty(moduleName)) return null;
            var module = designData.Modules.Find(moduleName);
            if (module == null) logError($"The current user module '{moduleName}' does not exist.");
            return module;
        }

        //アドレス・表示名は CurrentUser モジュールに置いた差出人契約 (MailSenderContractField) が宣言する
        internal static MailSenderContractFieldDesign? FindContract(ModuleDesign module, Action<string> logError)
        {
            var contract = Extras.Mail.MailContracts.Sender(module);
            if (contract == null)
                logError($"The current user module '{module.Name}' does not implement the mail sender contract. " +
                    "Put a MailSenderContractField on it and declare the mail address.");
            return contract;
        }
    }
}
