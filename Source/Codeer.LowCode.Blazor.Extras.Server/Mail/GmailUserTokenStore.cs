using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// Gmail ユーザー同意モードのユーザー単位トークン (差出人アドレス → CurrentUser モジュールの GmailTokenField 列)。
    /// Gmail 送信器 (GmailApiMailSender) にだけ結線する。他の送信インフラでは使わない。
    /// </summary>
    /// <remarks>
    /// トークン列は書き込み専用 (DbColumn IsWriteOnly。製品のデータ層は SELECT に含めない = クライアントに返さない) なので、
    /// パスワードハッシュのログイン照合と同じくサーバー側の SQL で読む。テーブル名・列名はデザイン (DbTable / DbColumn) から取り、
    /// 識別子の引用符とパラメータ接頭辞はデータソースの種類に合わせるので全 DB 共通で動く。実行は IDbAccessor (テンプレートの DbAccess)。
    /// </remarks>
    public class GmailUserTokenStore
    {
        readonly DesignData _designData;
        readonly IDbAccessor _db;
        readonly Action<string> _logError;

        public GmailUserTokenStore(DesignData designData, IDbAccessor db, Action<string> logError)
        {
            _designData = designData;
            _db = db;
            _logError = logError;
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
                var module = MailUserModule.Find(_designData, _logError);
                if (module == null) return null;
                var tokenFields = module.Fields.OfType<GmailTokenFieldDesign>().ToList();
                if (tokenFields.Count == 0) return null;
                if (tokenFields.Count > 1)
                    _logError($"Module '{module.Name}' has {tokenFields.Count} GmailTokenFields. The first one ('{tokenFields[0].Name}') is used.");

                var contract = MailUserModule.FindContract(module, _logError);
                if (contract == null) return null;

                var sql = CreateTokenSql(module, contract, tokenFields[0], out var parameters, mailAddress);
                if (sql == null) return null;
                var row = (await _db.QueryAsync(module.DataSourceName, sql,
                    parameters.ToDictionary(e => e.Key, e => new ParamAndRawDbTypeName { Value = e.Value }))).FirstOrDefault();

                var token = GetColumnText(row, TokenAlias);
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

        internal const string TokenAlias = "token_value";

        //トークン列を差出人アドレスで引く SQL。テーブル名・列名はデザイン (DbTable / DbColumn) から取り、
        //識別子の引用符とパラメータ接頭辞はデータソースの種類に合わせる
        internal string? CreateTokenSql(ModuleDesign module, MailSenderContractFieldDesign contract,
            GmailTokenFieldDesign tokenField, out Dictionary<string, object?> parameters, string mailAddress)
        {
            parameters = new Dictionary<string, object?>();
            if (string.IsNullOrEmpty(module.DbTable))
            {
                _logError($"The current user module '{module.Name}' has no DbTable.");
                return null;
            }
            if (string.IsNullOrEmpty(tokenField.DbColumnToken))
            {
                _logError($"GmailTokenField '{tokenField.Name}' of module '{module.Name}' has no DbColumnToken.");
                return null;
            }
            var emailColumn = GetOwnColumn(module, contract.Email);
            if (emailColumn == null)
            {
                _logError($"The mail address role '{contract.Email}' of module '{module.Name}' must be a field of the module itself " +
                    "with a DB column (link paths are not supported for the Gmail token lookup).");
                return null;
            }

            var type = _db.GetDataSource(module.DataSourceName)?.DataSourceType ?? DataSourceType.SQLite;
            var (qs, qe) = type switch
            {
                DataSourceType.SQLServer => ("[", "]"),
                DataSourceType.MySQL => ("`", "`"),
                _ => ("\"", "\""),
            };
            var parameterName = type == DataSourceType.Oracle ? ":p0" : "@p0";
            parameters[parameterName] = mailAddress;

            return $"SELECT {qs}{tokenField.DbColumnToken}{qe} AS {TokenAlias} FROM {qs}{module.DbTable}{qe} " +
                $"WHERE {qs}{emailColumn}{qe} = {parameterName}";
        }

        //自モジュールのフィールドの DB 列名 (リンクパスは対象外)
        static string? GetOwnColumn(ModuleDesign module, string variable)
        {
            if (string.IsNullOrEmpty(variable)) return null;
            var fieldName = new VariableName(variable).FieldName;
            if (fieldName.IsLink) return null;
            var column = (module.Fields.FirstOrDefault(e => e.Name == fieldName.FullName) as DbValueFieldDesignBase)?.DbColumn;
            return string.IsNullOrEmpty(column) ? null : column;
        }

        //列名の大文字小文字は DB により揺れる (Oracle は大文字) ので無視して読む
        static string? GetColumnText(IDictionary<string, object>? row, string column)
        {
            if (row == null) return null;
            var key = row.Keys.FirstOrDefault(k => string.Equals(k, column, StringComparison.OrdinalIgnoreCase));
            if (key == null) return null;
            var value = row[key];
            return value == null || value is DBNull ? null : value.ToString();
        }
    }
}
