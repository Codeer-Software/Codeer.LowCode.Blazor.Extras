using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>ログインで特定したアカウント (ユーザーモジュールの行)。Cookie には UserId (NameIdentifier) と DisplayName (Name) を積む。</summary>
    /// <param name="UserId">ユーザー ID (IdField の値)。</param>
    /// <param name="LoginName">ログイン ID。二要素認証の QR のアカウント名にも使う。</param>
    /// <param name="DisplayName">表示名。契約の DisplayName が空なら LoginName。</param>
    /// <param name="TwoFactorEmail">メールのワンタイムコードの送信先。契約の TwoFactorEmail が空 (無効) なら null。</param>
    public record LoginAccount(string UserId, string LoginName, string DisplayName, string? TwoFactorEmail = null);

    /// <summary>
    /// ログイン時のユーザー行の読み書き。表・列はユーザーモジュール (AppSettings.CurrentUserModuleDesignName) のデザインから引く:
    /// 表 = モジュールの DbTable、ユーザー ID = <see cref="IdFieldDesign"/> の列、ログイン ID / 外部 IdP の突き合わせ / 有効フラグ / 表示名 = <see cref="LoginAccountContractFieldDesign"/> の役割、
    /// ハッシュ / ソルト = <see cref="PasswordHashFieldDesign"/> の書き込み専用列 (あれば。無ければパスワードログインなし = 外部 IdP 専用)。
    /// ログイン時はまだ認証済みユーザーがいないためモジュールの読み書き (権限モデル) は通せず、ここだけが SQL で直接読む (TotpLogin と同じ作法)。
    /// <code>
    /// var accounts = LoginAccountStore.Create(designData, dataService.DbAccess);   // ユーザーモジュールに LoginAccountContractField が無ければ null
    /// var account = accounts?.HasPassword == true ? await accounts.VerifyPasswordAsync(loginInfo.Id, loginInfo.Password) : null;
    /// if (account == null) return Unauthorized();
    /// </code>
    /// </summary>
    public class LoginAccountStore
    {
        readonly IDbAccessor _db;
        readonly string _dataSourceName;
        readonly string _table;
        readonly string _idColumn;
        readonly string _loginNameColumn;
        readonly string _externalLoginNameColumn;
        readonly string? _isActiveColumn;
        readonly string? _displayNameColumn;
        readonly string? _twoFactorEmailColumn;
        readonly string? _hashColumn;
        readonly string? _saltColumn;

        /// <summary>ユーザーモジュールに <see cref="LoginAccountContractFieldDesign"/> があれば作る。無ければ null。</summary>
        public static LoginAccountStore? Create(DesignData designData, IDbAccessor db)
        {
            var module = designData.Modules.Find(designData.AppSettings.CurrentUserModuleDesignName);
            var contract = module?.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            if (module == null || contract == null) return null;
            return new LoginAccountStore(module, contract, module.Fields.OfType<PasswordHashFieldDesign>().FirstOrDefault(), db);
        }

        public LoginAccountStore(ModuleDesign userModule, LoginAccountContractFieldDesign contract, PasswordHashFieldDesign? passwordHash, IDbAccessor db)
        {
            var id = userModule.Fields.OfType<IdFieldDesign>().FirstOrDefault()
                ?? throw new InvalidOperationException($"LoginAccountStore: module '{userModule.Name}' has no IdField.");
            if (string.IsNullOrWhiteSpace(userModule.DbTable)) throw new InvalidOperationException($"LoginAccountStore: DbTable of module '{userModule.Name}' is not set.");
            if (string.IsNullOrWhiteSpace(id.DbColumn)) throw new InvalidOperationException($"LoginAccountStore: DbColumn of IdField '{id.Name}' in module '{userModule.Name}' is not set.");
            if (passwordHash != null && (string.IsNullOrWhiteSpace(passwordHash.DbColumnHash) || string.IsNullOrWhiteSpace(passwordHash.DbColumnSalt)))
                throw new InvalidOperationException($"LoginAccountStore: DbColumnHash / DbColumnSalt of PasswordHashField '{passwordHash.Name}' in module '{userModule.Name}' are not set.");

            _db = db;
            _dataSourceName = userModule.DataSourceName;
            _table = userModule.DbTable;
            _idColumn = id.DbColumn;
            _loginNameColumn = RoleColumn(userModule, contract, nameof(contract.LoginName), contract.LoginName)!;
            _externalLoginNameColumn = RoleColumn(userModule, contract, nameof(contract.ExternalLoginName), contract.ExternalLoginName) ?? _loginNameColumn;
            _isActiveColumn = RoleColumn(userModule, contract, nameof(contract.IsActive), contract.IsActive);
            _displayNameColumn = RoleColumn(userModule, contract, nameof(contract.DisplayName), contract.DisplayName);
            _twoFactorEmailColumn = RoleColumn(userModule, contract, nameof(contract.TwoFactorEmail), contract.TwoFactorEmail);
            _hashColumn = passwordHash?.DbColumnHash;
            _saltColumn = passwordHash?.DbColumnSalt;
        }

        //役割 → 自モジュールの DB 列。必須の役割が空なら例外、任意の役割が空なら null
        static string? RoleColumn(ModuleDesign module, LoginAccountContractFieldDesign contract, string role, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                if (role == nameof(contract.LoginName)) throw new InvalidOperationException($"LoginAccountStore: {role} of {contract.Name} in module '{module.Name}' is not set.");
                return null;
            }
            var field = module.Fields.OfType<DbValueFieldDesignBase>().FirstOrDefault(f => f.Name == fieldName)
                ?? throw new InvalidOperationException($"LoginAccountStore: '{fieldName}' ({role} of {contract.Name}) is not a DB field of module '{module.Name}'.");
            if (string.IsNullOrWhiteSpace(field.DbColumn)) throw new InvalidOperationException($"LoginAccountStore: DbColumn of field '{fieldName}' ({role} of {contract.Name}) is not set.");
            return field.DbColumn;
        }

        /// <summary>ユーザーモジュールに PasswordHashField があり、ID/パスワードのログインができるか。</summary>
        public bool HasPassword => _hashColumn != null;

        /// <summary>契約の TwoFactorEmail が設定され、メールのワンタイムコードによる二要素認証が有効か。</summary>
        public bool HasTwoFactorEmail => _twoFactorEmailColumn != null;

        /// <summary>
        /// ログイン ID とパスワードを照合する。ユーザーが無い・パスワード不一致・パスワード未設定 (外部 IdP 専用ユーザー)・停止中は
        /// どれも null (理由は外に出さない)。
        /// </summary>
        public async Task<LoginAccount?> VerifyPasswordAsync(string? loginName, string? password)
        {
            if (!HasPassword) throw new InvalidOperationException("LoginAccountStore: the user module has no PasswordHashField.");
            if (string.IsNullOrEmpty(loginName)) return null;
            var row = await FindRowAsync(_loginNameColumn, loginName);
            if (row == null || !IsActive(row)) return null;
            var hash = Convert.ToString(Value(row, _hashColumn!)) ?? string.Empty;
            var salt = Convert.ToString(Value(row, _saltColumn!)) ?? string.Empty;
            if (hash.Length == 0 || salt.Length == 0) return null;
            if (!PasswordHashHelper.VerifyHash(password ?? string.Empty, hash, salt)) return null;
            return ToAccount(row);
        }

        /// <summary>
        /// 外部 IdP が確認した本人 (Entra = UPN、Google / Cognito = メール) をアカウントに解決する。契約の ExternalLoginName の列 (空なら LoginName の列) で探す。
        /// 無い・停止中は null。
        /// </summary>
        public async Task<LoginAccount?> FindByExternalLoginNameAsync(string? externalLoginName)
        {
            if (string.IsNullOrEmpty(externalLoginName)) return null;
            var row = await FindRowAsync(_externalLoginNameColumn, externalLoginName);
            return row == null || !IsActive(row) ? null : ToAccount(row);
        }

        /// <summary>ユーザーが 1 人でもいるか (初回起動時の管理者作成の判定)。</summary>
        public async Task<bool> AnyAsync()
        {
            var (q, _) = Sql();
            var rows = await _db.QueryAsync(_dataSourceName, $"select count(*) as cnt from {q(_table)}", new());
            return Convert.ToInt64(rows.FirstOrDefault()?.Values.FirstOrDefault() ?? 0) > 0;
        }

        /// <summary>
        /// ログイン ID・パスワード (ハッシュ化して保存) でユーザー行を作る。初回起動時の管理者作成用。
        /// 表示名の列があれば同じ値、有効フラグの列があれば 1 を入れる。他の列は DB の既定値。
        /// </summary>
        public async Task AddAsync(string loginName, string password)
        {
            if (!HasPassword) throw new InvalidOperationException("LoginAccountStore: the user module has no PasswordHashField.");
            var (q, p) = Sql();
            var hashed = PasswordHashHelper.CreateHash(password);
            var columns = new List<string> { q(_loginNameColumn), q(_hashColumn!), q(_saltColumn!) };
            var values = new List<object?> { loginName, hashed.Hash ?? string.Empty, hashed.Salt ?? string.Empty };
            if (_displayNameColumn != null) { columns.Add(q(_displayNameColumn)); values.Add(loginName); }
            if (_isActiveColumn != null) { columns.Add(q(_isActiveColumn)); values.Add(1); }
            var parameters = values.Select((v, i) => (Key: $"{p}{i + 1}", Value: v)).ToList();
            var sql = $"insert into {q(_table)} ({string.Join(", ", columns)}) values ({string.Join(", ", parameters.Select(x => x.Key))})";
            await _db.ExecuteAsync(_dataSourceName, sql, parameters.ToDictionary(x => x.Key, x => x.Value));
        }

        async Task<IDictionary<string, object>?> FindRowAsync(string keyColumn, string key)
        {
            var (q, p) = Sql();
            var columns = new List<string> { q(_idColumn), q(_loginNameColumn) };
            if (_displayNameColumn != null) columns.Add(q(_displayNameColumn));
            if (_twoFactorEmailColumn != null) columns.Add(q(_twoFactorEmailColumn));
            if (_isActiveColumn != null) columns.Add(q(_isActiveColumn));
            if (HasPassword) { columns.Add(q(_hashColumn!)); columns.Add(q(_saltColumn!)); }
            var sql = $"select {string.Join(", ", columns.Distinct())} from {q(_table)} where {q(keyColumn)} = {p}1";
            return (await _db.QueryAsync(_dataSourceName, sql, new() { { p + "1", new ParamAndRawDbTypeName { Value = key } } })).FirstOrDefault();
        }

        bool IsActive(IDictionary<string, object> row)
        {
            if (_isActiveColumn == null) return true;
            var v = Value(row, _isActiveColumn);
            return v switch
            {
                null => false,
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
                _ => Convert.ToInt64(v) != 0,
            };
        }

        LoginAccount ToAccount(IDictionary<string, object> row)
        {
            var loginName = Convert.ToString(Value(row, _loginNameColumn)) ?? string.Empty;
            var displayName = _displayNameColumn == null ? null : Convert.ToString(Value(row, _displayNameColumn));
            var twoFactorEmail = _twoFactorEmailColumn == null ? null : Convert.ToString(Value(row, _twoFactorEmailColumn));
            return new(Convert.ToString(Value(row, _idColumn)) ?? string.Empty, loginName, string.IsNullOrEmpty(displayName) ? loginName : displayName, twoFactorEmail);
        }

        //識別子の引用符とパラメータ接頭辞は DB の種類で変わる (TemporaryFileManager と同じ)
        (Func<string, string> Quote, string ParameterPrefix) Sql()
        {
            var dataSource = _db.GetDataSource(_dataSourceName) ?? throw new InvalidOperationException($"LoginAccountStore: data source '{_dataSourceName}' not found.");
            var type = dataSource.DataSourceType;
            Func<string, string> quote = x => type == DataSourceType.SQLServer ? $"[{x}]" : type == DataSourceType.MySQL ? $"`{x}`" : $"\"{x}\"";
            return (quote, type == DataSourceType.Oracle ? ":p" : "@p");
        }

        static object? Value(IDictionary<string, object>? row, string column)
        {
            if (row == null) return null;
            if (row.TryGetValue(column, out var v)) return v is DBNull ? null : v;
            var key = row.Keys.FirstOrDefault(k => k.Equals(column, StringComparison.OrdinalIgnoreCase));
            return key == null || row[key] is DBNull ? null : row[key];
        }
    }
}
