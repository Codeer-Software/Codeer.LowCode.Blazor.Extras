using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// ID/パスワードのログインに足す二要素認証 (TOTP) の設定。appsettings のセクション名はアプリが決める (テンプレートは "TotpLogin")。
    /// 有効・無効と列はデザインで決まる: ユーザーモジュール (AppSettings.CurrentUserModuleDesignName) に
    /// <see cref="TotpSecretFieldDesign"/> を置くと有効。ここにあるのは表示用の設定だけ。
    /// </summary>
    public class TotpLoginSettings
    {
        /// <summary>オーセンティケータアプリに表示する発行者名 (アプリ名)。空なら "LowCodeApp"。</summary>
        public string Issuer { get; set; } = string.Empty;
    }

    /// <summary><see cref="TotpLoginResult.Status"/> の値。</summary>
    public static class TotpLoginStatus
    {
        /// <summary>検証成功。サインインしてよい。</summary>
        public const string Ok = "ok";
        /// <summary>登録済み。オーセンティケータのコードを送ってくること。</summary>
        public const string CodeRequired = "totp";
        /// <summary>未登録。QR を表示してオーセンティケータに登録させ、そのコードを送ってくること。</summary>
        public const string Setup = "setup";
        /// <summary>コード不一致 (期限切れ・再利用を含む)。</summary>
        public const string InvalidCode = "invalid_code";
    }

    /// <summary>ログインの 2 段階目の結果。<see cref="Status"/> が setup のときだけ登録用の情報が入る。</summary>
    public class TotpLoginResult
    {
        public string Status { get; set; } = string.Empty;
        /// <summary>手入力用の秘密鍵 (Base32)。</summary>
        public string? Secret { get; set; }
        public string? OtpauthUri { get; set; }
        /// <summary>QR コード (PNG, base64)。&lt;img src="data:image/png;base64,..."&gt; で表示する。</summary>
        public string? QrPngBase64 { get; set; }
    }

    /// <summary>ユーザー 1 人分の TOTP 登録状態 (ユーザー行の 3 列を読んだもの)。<see cref="TotpLogin.FindAsync"/> が返す。</summary>
    public class TotpSecret
    {
        public string UserId { get; set; } = string.Empty;
        public string Secret { get; set; } = string.Empty;
        /// <summary>オーセンティケータのコードで一度でも確認できたか。未確認の鍵は登録用 QR の再表示に使い回す。</summary>
        public bool IsConfirmed { get; set; }
        /// <summary>最後に成功したタイムステップ。同じコードの再利用 (リプレイ) を防ぐ。</summary>
        public long LastTimestep { get; set; }
    }

    /// <summary>
    /// ID/パスワード検証の後に呼ぶ二要素認証。パスワードの検証とサインインはアプリ (AccountController) が行い、
    /// ここはコードの検証・初回登録・リプレイ防止と、ユーザー行の TOTP 列の読み書きだけを持つ。
    /// 表・列はユーザーモジュールのデザイン (<see cref="IdFieldDesign"/> の列と <see cref="TotpSecretFieldDesign"/> の 3 列) から引く。
    /// 列は書き込み専用なので通常のモジュール読み書きには出てこず、ここだけが SQL で直接触る (パスワードハッシュと同じ作法)。
    /// <code>
    /// var totp = TotpLogin.Create(designData, SystemConfig.Instance.TotpLogin, dataService.DbAccess);   // フィールドが無ければ null
    /// if (totp != null)
    /// {
    ///     var result = await totp.VerifyAsync(user.Id, user.UserName, loginInfo.TotpCode);
    ///     if (result.Status != TotpLoginStatus.Ok) return Ok(result);   // setup / totp / invalid_code: サインインしない
    /// }
    /// </code>
    /// 登録は最初のログイン時 (パスワード成功後) に行う割り切り。パスワードだけを知る第三者が先に登録できる余地があるので、
    /// それが許容できない運用では初回ログインを管理者立ち会いで行う等で補う。リセットは <see cref="ResetAsync"/> (列を空にする)。
    /// </summary>
    public class TotpLogin
    {
        const string DefaultIssuer = "LowCodeApp";

        readonly string _issuer;
        readonly IDbAccessor _db;
        readonly string _dataSourceName;
        readonly string _table;
        readonly string _idColumn;
        readonly string _secretColumn;
        readonly string _confirmedColumn;
        readonly string _lastTimestepColumn;

        /// <summary>ユーザーモジュールに <see cref="TotpSecretFieldDesign"/> があれば作る。無ければ null (二要素認証なし)。</summary>
        public static TotpLogin? Create(DesignData designData, TotpLoginSettings settings, IDbAccessor db)
        {
            var module = designData.Modules.Find(designData.AppSettings.CurrentUserModuleDesignName);
            var field = module?.Fields.OfType<TotpSecretFieldDesign>().FirstOrDefault();
            if (module == null || field == null) return null;
            return new TotpLogin(module, field, settings, db);
        }

        public TotpLogin(ModuleDesign userModule, TotpSecretFieldDesign field, TotpLoginSettings settings, IDbAccessor db)
        {
            var id = userModule.Fields.OfType<IdFieldDesign>().FirstOrDefault()
                ?? throw new InvalidOperationException($"TotpLogin: module '{userModule.Name}' has no IdField.");
            foreach (var (name, value) in new[] { (nameof(userModule.DbTable), userModule.DbTable), (nameof(id.DbColumn), id.DbColumn),
                (nameof(field.DbColumnSecret), field.DbColumnSecret), (nameof(field.DbColumnConfirmed), field.DbColumnConfirmed), (nameof(field.DbColumnLastTimestep), field.DbColumnLastTimestep) })
            {
                if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"TotpLogin: {name} of module '{userModule.Name}' is not set.");
            }
            _issuer = string.IsNullOrWhiteSpace(settings.Issuer) ? DefaultIssuer : settings.Issuer;
            _db = db;
            _dataSourceName = userModule.DataSourceName;
            _table = userModule.DbTable;
            _idColumn = id.DbColumn;
            _secretColumn = field.DbColumnSecret;
            _confirmedColumn = field.DbColumnConfirmed;
            _lastTimestepColumn = field.DbColumnLastTimestep;
        }

        /// <summary>
        /// 2 段階目。code が空なら状態を返すだけ (登録済み = totp / 未登録 = setup と登録情報)。
        /// code があれば検証し、成功したときだけ Ok を返す (初回は登録確定、以降はリプレイ防止のステップ更新)。
        /// </summary>
        public async Task<TotpLoginResult> VerifyAsync(string userId, string accountName, string? code)
        {
            var current = await FindAsync(userId);

            if (string.IsNullOrWhiteSpace(code))
            {
                if (current?.IsConfirmed == true) return new TotpLoginResult { Status = TotpLoginStatus.CodeRequired };

                //未登録 (または登録が未確認のまま): 秘密鍵を発行して登録用情報を返す。確認まで同じ鍵を使い回す
                var secret = current?.Secret ?? Totp.CreateSecret();
                if (current == null) await SaveNewAsync(userId, secret);

                var uri = Totp.CreateOtpauthUri(_issuer, accountName, secret);
                return new TotpLoginResult
                {
                    Status = TotpLoginStatus.Setup,
                    Secret = secret,
                    OtpauthUri = uri,
                    QrPngBase64 = CreateQrPngBase64(uri),
                };
            }

            if (current == null) return new TotpLoginResult { Status = TotpLoginStatus.InvalidCode };

            var timestep = Totp.VerifyCode(current.Secret, code.Trim());
            if (timestep == null || timestep <= current.LastTimestep) return new TotpLoginResult { Status = TotpLoginStatus.InvalidCode };

            await MarkVerifiedAsync(userId, timestep.Value);
            return new TotpLoginResult { Status = TotpLoginStatus.Ok };
        }

        /// <summary>ユーザーの TOTP 登録状態。未登録 (秘密鍵の列が空) なら null。</summary>
        public async Task<TotpSecret?> FindAsync(string userId)
        {
            var (q, p) = Sql();
            var sql = $"select {q(_secretColumn)}, {q(_confirmedColumn)}, {q(_lastTimestepColumn)} from {q(_table)} where {q(_idColumn)} = {p}1";
            var row = (await _db.QueryAsync(_dataSourceName, sql, new() { { p + "1", new ParamAndRawDbTypeName { Value = userId } } })).FirstOrDefault();
            var secret = Convert.ToString(Value(row, _secretColumn));
            if (row == null || string.IsNullOrEmpty(secret)) return null;
            return new TotpSecret
            {
                UserId = userId,
                Secret = secret,
                IsConfirmed = Convert.ToInt64(Value(row, _confirmedColumn) ?? 0) != 0,
                LastTimestep = Convert.ToInt64(Value(row, _lastTimestepColumn) ?? 0),
            };
        }

        /// <summary>登録を消す (機種変更・紛失時のリセット)。次のログインで再登録になる。</summary>
        public async Task ResetAsync(string userId)
        {
            var (q, p) = Sql();
            var sql = $"update {q(_table)} set {q(_secretColumn)} = null, {q(_confirmedColumn)} = 0, {q(_lastTimestepColumn)} = 0 where {q(_idColumn)} = {p}1";
            await _db.ExecuteAsync(_dataSourceName, sql, new() { { p + "1", userId } });
        }

        async Task SaveNewAsync(string userId, string secret)
        {
            var (q, p) = Sql();
            var sql = $"update {q(_table)} set {q(_secretColumn)} = {p}1, {q(_confirmedColumn)} = 0, {q(_lastTimestepColumn)} = 0 where {q(_idColumn)} = {p}2";
            await _db.ExecuteAsync(_dataSourceName, sql, new() { { p + "1", secret }, { p + "2", userId } });
        }

        async Task MarkVerifiedAsync(string userId, long timestep)
        {
            var (q, p) = Sql();
            var sql = $"update {q(_table)} set {q(_confirmedColumn)} = 1, {q(_lastTimestepColumn)} = {p}1 where {q(_idColumn)} = {p}2";
            await _db.ExecuteAsync(_dataSourceName, sql, new() { { p + "1", timestep }, { p + "2", userId } });
        }

        //識別子の引用符とパラメータ接頭辞は DB の種類で変わる (TemporaryFileManager と同じ)
        (Func<string, string> Quote, string ParameterPrefix) Sql()
        {
            var dataSource = _db.GetDataSource(_dataSourceName) ?? throw new InvalidOperationException($"TotpLogin: data source '{_dataSourceName}' not found.");
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

        static string CreateQrPngBase64(string uri)
        {
            using var generator = new QRCoder.QRCodeGenerator();
            using var data = generator.CreateQrCode(uri, QRCoder.QRCodeGenerator.ECCLevel.M);
            return Convert.ToBase64String(new QRCoder.PngByteQRCode(data).GetGraphic(6));
        }
    }
}
