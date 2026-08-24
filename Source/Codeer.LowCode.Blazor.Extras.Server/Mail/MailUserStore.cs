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
    /// ユーザーモジュール (デザインの AppSettings.CurrentUserModuleDesignName = CurrentUser のモジュール) の検索。
    /// ①操作ユーザーの差出人情報 (「自分を差出人にする」= IsFromCurrentUser)、
    /// ②GmailApi ユーザー同意モードのユーザー単位トークン (差出人アドレス → GmailTokenField 列)。
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
            if (string.IsNullOrEmpty(userId)) return null;
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

                var emailColumn = GetColumn(module, _config.UserEmailFieldName);
                var tokenColumn = tokenFields[0].DbColumnToken;
                if (string.IsNullOrEmpty(emailColumn) || string.IsNullOrEmpty(tokenColumn))
                {
                    _logError($"Mail.UserEmailFieldName '{_config.UserEmailFieldName}' or the token column of '{tokenFields[0].Name}' is not resolvable on module '{module.Name}'.");
                    return null;
                }

                var rows = await _dbAccessor.QueryAsync(module.DataSourceName,
                    $"select {tokenColumn} from {module.DbTable} where {emailColumn} = @mailAddress",
                    new Dictionary<string, ParamAndRawDbTypeName> { ["mailAddress"] = new() { Value = mailAddress } });
                var token = rows.Select(e => e.Values.FirstOrDefault()?.ToString())
                    .FirstOrDefault(e => !string.IsNullOrEmpty(e));
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

        static string? GetColumn(ModuleDesign module, string fieldName)
            => string.IsNullOrEmpty(fieldName)
                ? null
                : (module.Fields.FirstOrDefault(e => e.Name == fieldName) as DbValueFieldDesignBase)?.DbColumn;
    }
}
