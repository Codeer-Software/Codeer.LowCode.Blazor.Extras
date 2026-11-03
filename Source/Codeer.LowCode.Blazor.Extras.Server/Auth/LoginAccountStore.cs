using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>ログインで特定したアカウント (ユーザーモジュールの行)。Cookie には UserId (NameIdentifier) と DisplayName (Name) と SessionStamp を積む。</summary>
    /// <param name="UserId">ユーザー ID (IdField の値)。</param>
    /// <param name="LoginName">ログイン ID。二要素認証の QR のアカウント名にも使う。</param>
    /// <param name="DisplayName">表示名。契約の DisplayName が空なら LoginName。</param>
    /// <param name="TwoFactorEmail">メールのワンタイムコードの送信先。契約の TwoFactorEmail が空 (無効) なら null。</param>
    /// <param name="SessionStamp">本人確認した時点のセッションの印 (契約の DbColumnSessionStamp の値。列が無ければ null)。そのまま Cookie に積む。</param>
    public record LoginAccount(string UserId, string LoginName, string DisplayName, string? TwoFactorEmail = null, string? SessionStamp = null);

    /// <summary>
    /// ログイン時のユーザー行の読み書き。表・列はユーザーモジュール (AppSettings.CurrentUserModuleDesignName) のデザインから引く:
    /// 表 = モジュールの DbTable、ユーザー ID = <see cref="IdFieldDesign"/> の列、ログイン ID / 外部 IdP の突き合わせ / 有効フラグ / 表示名 = <see cref="LoginAccountContractFieldDesign"/> の役割、
    /// ハッシュ / ソルト = 契約の DbColumnPasswordHash / DbColumnPasswordSalt (書き込み専用。空ならパスワードログインなし = 外部 IdP 専用。書く側は PasswordHashField)、
    /// セッションの印 = 契約の DbColumnSessionStamp (書き込み専用。空なら Cookie の照合なし)。
    /// ログイン時はまだ認証済みユーザーがいないためモジュールの読み書き (権限モデル) は通せず、ここだけが SQL で直接読む (TotpLogin と同じ作法)。
    /// <code>
    /// var accounts = LoginAccountStore.Create(designData, dataService.DbAccess);   // ユーザーモジュールに LoginAccountContractField が無ければ null
    /// var account = accounts?.HasPassword == true ? await accounts.VerifyPasswordAsync(loginInfo.Id, loginInfo.Password) : null;
    /// if (account == null) return Unauthorized();
    /// </code>
    /// セッションの印: ログインで読んだ印を Cookie に積み、リクエストごとに <see cref="IsSessionValidAsync"/> で今の行と照合する (<see cref="LoginSessionValidation"/>)。
    /// <see cref="RevokeSessionsAsync"/> で新しい印に書き換えると、そのユーザーのそれまでの Cookie は全部次の要求から落ちる (全端末ログアウト)。
    /// パスワードの変更 (PasswordHashHelper.ApplyPasswordHash) と有効フラグを偽にする保存 (PasswordHashingModuleDataIO) も印を書き換える。
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
        readonly string? _sessionStampColumn;
        readonly string? _logicalDeleteColumn;   //論理削除のフラグ列 (SystemFieldNames.LogicalDelete)
        readonly string? _deletedMarkerColumn;   //LogicalDelete が無いときの削除の印 (DeletedAt / Deleter。値が入っていれば削除済み)

        /// <summary>ユーザーモジュールに <see cref="LoginAccountContractFieldDesign"/> があれば作る。無ければ null。</summary>
        public static LoginAccountStore? Create(DesignData designData, IDbAccessor db)
        {
            var module = designData.Modules.Find(designData.AppSettings.CurrentUserModuleDesignName);
            var contract = module?.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            if (module == null || contract == null) return null;
            return new LoginAccountStore(module, contract, db);
        }

        public LoginAccountStore(ModuleDesign userModule, LoginAccountContractFieldDesign contract, IDbAccessor db)
        {
            var id = userModule.Fields.OfType<IdFieldDesign>().FirstOrDefault()
                ?? throw new InvalidOperationException($"LoginAccountStore: module '{userModule.Name}' has no IdField.");
            if (string.IsNullOrWhiteSpace(userModule.DbTable)) throw new InvalidOperationException($"LoginAccountStore: DbTable of module '{userModule.Name}' is not set.");
            if (string.IsNullOrWhiteSpace(id.DbColumn)) throw new InvalidOperationException($"LoginAccountStore: DbColumn of IdField '{id.Name}' in module '{userModule.Name}' is not set.");
            if (string.IsNullOrWhiteSpace(contract.DbColumnPasswordHash) != string.IsNullOrWhiteSpace(contract.DbColumnPasswordSalt))
                throw new InvalidOperationException($"LoginAccountStore: set both DbColumnPasswordHash and DbColumnPasswordSalt of {contract.Name} in module '{userModule.Name}', or neither.");

            _db = db;
            _dataSourceName = userModule.DataSourceName;
            _table = userModule.DbTable;
            _idColumn = id.DbColumn;
            _loginNameColumn = RoleColumn(userModule, contract, nameof(contract.LoginName), contract.LoginName)!;
            _externalLoginNameColumn = RoleColumn(userModule, contract, nameof(contract.ExternalLoginName), contract.ExternalLoginName) ?? _loginNameColumn;
            _isActiveColumn = RoleColumn(userModule, contract, nameof(contract.IsActive), contract.IsActive);
            _displayNameColumn = RoleColumn(userModule, contract, nameof(contract.DisplayName), contract.DisplayName);
            _twoFactorEmailColumn = RoleColumn(userModule, contract, nameof(contract.TwoFactorEmail), contract.TwoFactorEmail);
            _hashColumn = contract.HasPassword ? contract.DbColumnPasswordHash : null;
            _saltColumn = contract.HasPassword ? contract.DbColumnPasswordSalt : null;
            _sessionStampColumn = contract.HasSessionStamp ? contract.DbColumnSessionStamp : null;
            _logicalDeleteColumn = DbColumnOf(userModule, SystemFieldNames.LogicalDelete);
            _deletedMarkerColumn = _logicalDeleteColumn != null ? null : DbColumnOf(userModule, SystemFieldNames.DeletedAt) ?? DbColumnOf(userModule, SystemFieldNames.Deleter);
        }

        static string? DbColumnOf(ModuleDesign module, string fieldName)
        {
            var field = module.Fields.OfType<DbValueFieldDesignBase>().FirstOrDefault(f => f.Name == fieldName);
            return field == null || string.IsNullOrWhiteSpace(field.DbColumn) ? null : field.DbColumn;
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

        /// <summary>契約にパスワードのハッシュ / ソルト列があり、ID/パスワードのログインができるか。</summary>
        public bool HasPassword => _hashColumn != null;

        /// <summary>契約の TwoFactorEmail が設定され、メールのワンタイムコードによる二要素認証が有効か。</summary>
        public bool HasTwoFactorEmail => _twoFactorEmailColumn != null;

        /// <summary>契約にセッションの印の列 (DbColumnSessionStamp) があり、ログイン済み Cookie の照合 (失効) が有効か。</summary>
        public bool HasSessionStamp => _sessionStampColumn != null;

        /// <summary>
        /// ログイン ID とパスワードを照合する。ユーザーが無い・パスワード不一致・パスワード未設定 (外部 IdP 専用ユーザー)・停止中は
        /// どれも null (理由は外に出さない)。印の列があって行の印が空なら採番して書く (印を使い始める前からいるユーザー)。
        /// </summary>
        public async Task<LoginAccount?> VerifyPasswordAsync(string? loginName, string? password)
        {
            if (!HasPassword) throw new InvalidOperationException("LoginAccountStore: the login account contract has no password columns.");
            if (string.IsNullOrEmpty(loginName)) return null;
            var row = await FindRowAsync(_loginNameColumn, loginName);
            if (row == null || !IsActive(row)) return null;
            var hash = Convert.ToString(Value(row, _hashColumn!)) ?? string.Empty;
            var salt = Convert.ToString(Value(row, _saltColumn!)) ?? string.Empty;
            if (hash.Length == 0 || salt.Length == 0) return null;
            if (!PasswordHashHelper.VerifyHash(password ?? string.Empty, hash, salt)) return null;
            return await ToLoginAccountAsync(row);
        }

        /// <summary>
        /// ログイン ID でアカウントを探す (パスワードは照合しない)。無い・停止中は null。
        /// 総当たり対策 (LoginLockout) がアカウントの Id でも数えるために使う。
        /// </summary>
        public async Task<LoginAccount?> FindByLoginNameAsync(string? loginName)
        {
            if (string.IsNullOrEmpty(loginName)) return null;
            var row = await FindRowAsync(_loginNameColumn, loginName);
            return row == null || !IsActive(row) ? null : ToAccount(row);
        }

        /// <summary>
        /// 外部 IdP が確認した本人 (Entra = UPN、Google / Cognito = メール) をアカウントに解決する。契約の ExternalLoginName の列 (空なら LoginName の列) で探す。
        /// 無い・停止中は null。印の列があって行の印が空なら採番して書く。
        /// </summary>
        public async Task<LoginAccount?> FindByExternalLoginNameAsync(string? externalLoginName)
        {
            if (string.IsNullOrEmpty(externalLoginName)) return null;
            var row = await FindRowAsync(_externalLoginNameColumn, externalLoginName);
            return row == null || !IsActive(row) ? null : await ToLoginAccountAsync(row);
        }

        /// <summary>
        /// Cookie に積んだ印が今も有効か (リクエストごとの照合。<see cref="LoginSessionValidation"/> から呼ぶ)。
        /// 行が無い (論理削除済みを含む)・停止中・行の印が空・Cookie の印が空・不一致はどれも false。印の列が無い契約では呼べない (例外)。
        /// 読めなかったとき (DB の障害) は例外のまま = 無効と区別する。
        /// </summary>
        public async Task<bool> IsSessionValidAsync(string? userId, string? sessionStamp)
        {
            if (!HasSessionStamp) throw new InvalidOperationException("LoginAccountStore: the login account contract has no DbColumnSessionStamp.");
            if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(sessionStamp)) return false;
            var id = await IdParameterAsync(userId);
            if (id == null) return false;

            var (q, p) = Sql();
            var columns = new List<string> { q(_sessionStampColumn!) };
            if (_isActiveColumn != null) columns.Add(q(_isActiveColumn));
            var sql = $"select {string.Join(", ", columns)} from {q(_table)} where {q(_idColumn)} = {p}1{NotDeleted()}";
            var row = (await _db.QueryAsync(_dataSourceName, sql, new() { { p + "1", new ParamAndRawDbTypeName { Value = id } } })).FirstOrDefault();
            if (row == null || !IsActive(row)) return false;
            var current = Convert.ToString(Value(row, _sessionStampColumn!));
            return !string.IsNullOrEmpty(current) && string.Equals(current, sessionStamp, StringComparison.Ordinal);
        }

        /// <summary>
        /// そのユーザーのセッションの印を新しい値に書き換える (= 発行済みの Cookie を全部失効させる。全端末ログアウト)。
        /// 新しいログインは止めない (止めるなら有効フラグを偽にする)。進行中の保存の中から呼べば同じトランザクションに入る。
        /// 戻り値は新しい印 (行が無ければ null)。印の列が無い契約では呼べない (例外)。
        /// </summary>
        public async Task<string?> RevokeSessionsAsync(string userId)
        {
            if (!HasSessionStamp) throw new InvalidOperationException("LoginAccountStore: the login account contract has no DbColumnSessionStamp.");
            if (string.IsNullOrEmpty(userId)) return null;
            var id = await IdParameterAsync(userId);
            if (id == null) return null;

            var (q, p) = Sql();
            var stamp = SessionStamp.Create();
            var sql = $"update {q(_table)} set {q(_sessionStampColumn!)} = {p}1 where {q(_idColumn)} = {p}2";
            var count = await _db.ExecuteAsync(_dataSourceName, sql, new() { { p + "1", stamp }, { p + "2", id } });
            return count > 0 ? stamp : null;
        }

        /// <summary>ユーザーが 1 人でもいるか。</summary>
        public async Task<bool> AnyAsync()
        {
            var (q, _) = Sql();
            var rows = await _db.QueryAsync(_dataSourceName, $"select count(*) as cnt from {q(_table)}", new());
            return Convert.ToInt64(rows.FirstOrDefault()?.Values.FirstOrDefault() ?? 0) > 0;
        }

        /// <summary>
        /// ログイン ID・パスワード (ハッシュ化して保存) でユーザー行を作る (ホストが最初のユーザーを入れるとき用)。
        /// 表示名の列があれば同じ値、有効フラグの列があれば 1 を入れる。他の列は DB の既定値 (印は最初のログインで採番する)。
        /// 戻り値は作った行の Id (監査ログの対象に使う)。
        /// </summary>
        public async Task<string> AddAsync(string loginName, string password)
        {
            if (!HasPassword) throw new InvalidOperationException("LoginAccountStore: the login account contract has no password columns.");
            var (q, p) = Sql();
            var hashed = PasswordHashHelper.CreateHash(password);
            var columns = new List<string> { q(_loginNameColumn), q(_hashColumn!), q(_saltColumn!) };
            var values = new List<object?> { loginName, hashed.Hash ?? string.Empty, hashed.Salt ?? string.Empty };
            if (_displayNameColumn != null) { columns.Add(q(_displayNameColumn)); values.Add(loginName); }
            if (_isActiveColumn != null) { columns.Add(q(_isActiveColumn)); values.Add(1); }
            var parameters = values.Select((v, i) => (Key: $"{p}{i + 1}", Value: v)).ToList();
            var sql = $"insert into {q(_table)} ({string.Join(", ", columns)}) values ({string.Join(", ", parameters.Select(x => x.Key))})";
            await _db.ExecuteAsync(_dataSourceName, sql, parameters.ToDictionary(x => x.Key, x => x.Value));
            var row = await FindRowAsync(_loginNameColumn, loginName);
            return row == null ? string.Empty : ToAccount(row).UserId;
        }

        async Task<IDictionary<string, object>?> FindRowAsync(string keyColumn, string key)
        {
            var (q, p) = Sql();
            var columns = new List<string> { q(_idColumn), q(_loginNameColumn) };
            if (_displayNameColumn != null) columns.Add(q(_displayNameColumn));
            if (_twoFactorEmailColumn != null) columns.Add(q(_twoFactorEmailColumn));
            if (_isActiveColumn != null) columns.Add(q(_isActiveColumn));
            if (HasPassword) { columns.Add(q(_hashColumn!)); columns.Add(q(_saltColumn!)); }
            if (_sessionStampColumn != null) columns.Add(q(_sessionStampColumn));
            var sql = $"select {string.Join(", ", columns.Distinct())} from {q(_table)} where {q(keyColumn)} = {p}1{NotDeleted()}";
            return (await _db.QueryAsync(_dataSourceName, sql, new() { { p + "1", new ParamAndRawDbTypeName { Value = key } } })).FirstOrDefault();
        }

        //削除済みの行を除く WHERE の断片 (本体の読み出しと同じ判定: LogicalDelete 優先、無ければ DeletedAt / Deleter)。論理削除したユーザーはログインも Cookie の照合も通らない
        string NotDeleted()
        {
            var (q, _) = Sql();
            if (_logicalDeleteColumn != null)
            {
                var falseLiteral = GetDataSourceType() == DataSourceType.PostgreSQL ? "false" : "0";
                return $" and ({q(_logicalDeleteColumn)} is null or {q(_logicalDeleteColumn)} = {falseLiteral})";
            }
            return _deletedMarkerColumn == null ? string.Empty : $" and {q(_deletedMarkerColumn)} is null";
        }

        bool IsActive(IDictionary<string, object> row)
            => _isActiveColumn == null || IsActiveValue(Value(row, _isActiveColumn));

        /// <summary>有効フラグの値の読み方 (Boolean / 0・1 の数値 / "true"・"1" の文字列。NULL は無効)。保存時の判定 (PasswordHashingModuleDataIO) も同じ読み方をする。</summary>
        internal static bool IsActiveValue(object? v)
            => v switch
            {
                null => false,
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1",
                _ => Convert.ToInt64(v) != 0,
            };

        LoginAccount ToAccount(IDictionary<string, object> row)
        {
            var loginName = Convert.ToString(Value(row, _loginNameColumn)) ?? string.Empty;
            var displayName = _displayNameColumn == null ? null : Convert.ToString(Value(row, _displayNameColumn));
            var twoFactorEmail = _twoFactorEmailColumn == null ? null : Convert.ToString(Value(row, _twoFactorEmailColumn));
            var sessionStamp = _sessionStampColumn == null ? null : Convert.ToString(Value(row, _sessionStampColumn));
            return new(Convert.ToString(Value(row, _idColumn)) ?? string.Empty, loginName, string.IsNullOrEmpty(displayName) ? loginName : displayName, twoFactorEmail,
                string.IsNullOrEmpty(sessionStamp) ? null : sessionStamp);
        }

        //ログインの成立 (本人確認が通った行) で使う。印の列があるのに行の印が空なら採番して書き、その値を返す
        //(印を使い始める前からいるユーザー・LoginAccountStore.AddAsync や SQL で入れた行。印の無い Cookie は照合に落ちるので、採番前の Cookie が生き返ることはない)
        async Task<LoginAccount> ToLoginAccountAsync(IDictionary<string, object> row)
        {
            var account = ToAccount(row);
            if (_sessionStampColumn == null || account.SessionStamp != null) return account;
            //採番できなければログインを失敗させる (印の無い Cookie を発行すると、ログインは成功するのに次の要求から全部 401 になり原因が見えない)
            var stamp = await RevokeSessionsAsync(account.UserId)
                ?? throw new InvalidOperationException($"LoginAccountStore: could not assign a session stamp to user '{account.UserId}' (table '{_table}', column '{_sessionStampColumn}').");
            return account with { SessionStamp = stamp };
        }

        //識別子の引用符とパラメータ接頭辞は DB の種類で変わる (TemporaryFileManager と同じ)
        (Func<string, string> Quote, string ParameterPrefix) Sql()
        {
            var type = GetDataSourceType();
            Func<string, string> quote = x => type == DataSourceType.SQLServer ? $"[{x}]" : type == DataSourceType.MySQL ? $"`{x}`" : $"\"{x}\"";
            return (quote, type == DataSourceType.Oracle ? ":p" : "@p");
        }

        DataSourceType GetDataSourceType()
            => (_db.GetDataSource(_dataSourceName) ?? throw new InvalidOperationException($"LoginAccountStore: data source '{_dataSourceName}' not found.")).DataSourceType;

        //Cookie の文字列 Id を Id 列の型 (整数の自動採番・GUID・文字列) に合わせたパラメータ値にする。PostgreSQL は integer = text を許さないので型を合わせる。
        //列の型は接続ごとに 1 度だけ調べる (行を読まないスキーマだけの SELECT)。変換できない Id (壊れた Cookie) は null = 無効
        static readonly ConcurrentDictionary<string, Type> _idColumnTypes = new();

        async Task<object?> IdParameterAsync(string userId)
        {
            var type = await GetIdColumnTypeAsync();
            try
            {
                if (type == typeof(string)) return userId;
                if (type == typeof(Guid)) return Guid.Parse(userId);
                if (type == typeof(long) || type == typeof(int) || type == typeof(short) || type == typeof(decimal) || type == typeof(double) || type == typeof(float) || type == typeof(byte))
                    return Convert.ChangeType(decimal.Parse(userId, System.Globalization.CultureInfo.InvariantCulture), type, System.Globalization.CultureInfo.InvariantCulture);
                return userId;
            }
            catch (FormatException) { return null; }
            catch (OverflowException) { return null; }
        }

        async Task<Type> GetIdColumnTypeAsync()
        {
            var dataSource = _db.GetDataSource(_dataSourceName) ?? throw new InvalidOperationException($"LoginAccountStore: data source '{_dataSourceName}' not found.");
            var key = $"{dataSource.DataSourceType}|{dataSource.ConnectionString}|{_table}|{_idColumn}";
            if (_idColumnTypes.TryGetValue(key, out var cached)) return cached;

            var (q, _) = Sql();
            var connection = _db.GetConnection(_dataSourceName);
            await using var command = connection.CreateCommand();
            command.Transaction = _db.GetTransaction(_dataSourceName) as DbTransaction;
            command.CommandText = $"select {q(_idColumn)} from {q(_table)} where 1 = 0";
            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SchemaOnly);
            var type = reader.FieldCount > 0 ? reader.GetFieldType(0) : typeof(string);
            _idColumnTypes[key] = type;
            return type;
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
