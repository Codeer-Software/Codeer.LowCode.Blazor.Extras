using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>「自分を差出人にする」で解決した操作ユーザーの情報。</summary>
    public class MailCurrentUser
    {
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    /// <summary>
    /// ユーザーモジュール (Mail.UserModuleName / UserEmailFieldName / UserNameFieldName) の検索。
    /// ①操作ユーザーの差出人情報 (「自分を差出人にする」= IsFromCurrentUser)、
    /// ②GmailApi ユーザー同意モードのユーザー単位トークン (差出人アドレス → MailTokenField 列)。
    /// トークン列は書き込み専用 (クライアントに返さない) のため、通常のデータ取得経路ではなく
    /// サーバー内部の SQL で直接読む。
    /// </summary>
    public class MailUserStore
    {
        readonly DesignData _designData;
        readonly MailConfig _config;
        readonly IDbAccessor _dbAccessor;
        readonly Action<string> _logError;

        public MailUserStore(DesignData designData, MailConfig config, IDbAccessor dbAccessor, Action<string> logError)
        {
            _designData = designData;
            _config = config;
            _dbAccessor = dbAccessor;
            _logError = logError;
        }

        /// <summary>
        /// 操作ユーザー (認証コンテキストのユーザーId) の差出人情報を返す。
        /// 未設定・行なし・メール未登録は null (呼び出し側が失敗にする)。
        /// </summary>
        public async Task<MailCurrentUser?> FindCurrentUserAsync(string userId)
        {
            if (string.IsNullOrEmpty(_config.UserModuleName) || string.IsNullOrEmpty(userId)) return null;
            try
            {
                var module = FindUserModule();
                if (module == null) return null;
                var idColumn = GetColumn(module, SystemFieldNames.Id);
                var emailColumn = GetColumn(module, _config.UserEmailFieldName);
                if (string.IsNullOrEmpty(idColumn) || string.IsNullOrEmpty(emailColumn))
                {
                    _logError($"Mail.UserEmailFieldName '{_config.UserEmailFieldName}' is not resolvable on module '{module.Name}'.");
                    return null;
                }
                var nameColumn = GetColumn(module, _config.UserNameFieldName);
                var select = string.IsNullOrEmpty(nameColumn) ? emailColumn : $"{emailColumn}, {nameColumn}";

                var rows = await _dbAccessor.QueryAsync(module.DataSourceName,
                    $"select {select} from {module.DbTable} where {idColumn} = @userId",
                    new Dictionary<string, ParamAndRawDbTypeName> { ["userId"] = new() { Value = userId } });
                var row = rows.FirstOrDefault();
                var email = row?.Values.FirstOrDefault()?.ToString();
                if (string.IsNullOrEmpty(email)) return null;
                return new MailCurrentUser
                {
                    Email = email,
                    DisplayName = row!.Values.Skip(1).FirstOrDefault()?.ToString() ?? string.Empty,
                };
            }
            catch (Exception ex)
            {
                _logError($"Current user lookup failed for '{userId}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 差出人アドレスのユーザートークンを返す。未登録・設定不備は null (呼び出し側がシステムトークンにフォールバック)。
        /// 検索失敗はエラーログを出して null (送信自体は止めない)。
        /// </summary>
        public async Task<string?> FindRefreshTokenAsync(string mailAddress, string tokenFieldName)
        {
            if (string.IsNullOrEmpty(_config.UserModuleName) || string.IsNullOrEmpty(tokenFieldName) ||
                string.IsNullOrEmpty(mailAddress)) return null;
            try
            {
                var module = FindUserModule();
                if (module == null) return null;
                var emailColumn = GetColumn(module, _config.UserEmailFieldName);
                var tokenColumn = (module.Fields.FirstOrDefault(e => e.Name == tokenFieldName) as MailTokenFieldDesign)?.DbColumnToken;
                if (string.IsNullOrEmpty(emailColumn) || string.IsNullOrEmpty(tokenColumn))
                {
                    _logError($"Mail.UserEmailFieldName '{_config.UserEmailFieldName}' or UserTokenFieldName '{tokenFieldName}' is not resolvable on module '{module.Name}'.");
                    return null;
                }

                var rows = await _dbAccessor.QueryAsync(module.DataSourceName,
                    $"select {tokenColumn} from {module.DbTable} where {emailColumn} = @mailAddress",
                    new Dictionary<string, ParamAndRawDbTypeName> { ["mailAddress"] = new() { Value = mailAddress } });
                var token = rows.Select(e => e.Values.FirstOrDefault()?.ToString())
                    .FirstOrDefault(e => !string.IsNullOrEmpty(e));
                return string.IsNullOrEmpty(token) ? null : token;
            }
            catch (Exception ex)
            {
                _logError($"User token lookup failed for '{mailAddress}': {ex.Message}");
                return null;
            }
        }

        ModuleDesign? FindUserModule()
        {
            var module = _designData.Modules.Find(_config.UserModuleName);
            if (module == null) _logError($"Mail.UserModuleName '{_config.UserModuleName}' does not exist.");
            return module;
        }

        static string? GetColumn(ModuleDesign module, string fieldName)
            => string.IsNullOrEmpty(fieldName)
                ? null
                : (module.Fields.FirstOrDefault(e => e.Name == fieldName) as DbValueFieldDesignBase)?.DbColumn;
    }
}
