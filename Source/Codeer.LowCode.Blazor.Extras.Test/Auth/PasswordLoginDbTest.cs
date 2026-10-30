using Codeer.LowCode.Blazor.DbAccess;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Server.Auth;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.SystemSettings;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System.Text.RegularExpressions;

using Codeer.LowCode.Blazor.Extras.Test.Harness;

namespace Codeer.LowCode.Blazor.Extras.Test.Auth
{
    /// <summary>
    /// PasswordLogin (ホストの AccountController.Login が呼ぶ検証の流れ) を実 DB (SQLite) で。
    /// 契約の有無・パスワードの照合・総当たり対策の数え方 (不一致は残す / 二要素のコード要求と例外は数えない / 成功で数え直す)・
    /// 認証アプリとメールの二要素認証・クライアントへ返すもの・監査の補足 (Detail / IsDenied) を確かめる。
    /// </summary>
    public class PasswordLoginDbTest
    {
        const string Ds = "Main";
        const int MaxFailed = 3;
        string _dbFile = string.Empty;
        DbAccessor _db = null!;
        IDistributedCache _cache = null!;
        readonly List<MailMessage> _sent = new();
        bool _sendFails;

        static DesignData CreateDesign(bool withPassword = true, bool withTotp = false, bool withTwoFactorEmail = false)
        {
            var d = new DesignData();
            d.AppSettings.CurrentUserModuleDesignName = "AppUser";
            var user = new ModuleDesign { Name = "AppUser", DataSourceName = Ds, DbTable = "app_users" };
            user.Fields.Add(new IdFieldDesign { Name = "Id", DbColumn = "id" });
            user.Fields.Add(new TextFieldDesign { Name = "UserName", DbColumn = "user_name" });
            user.Fields.Add(new TextFieldDesign { Name = "Name", DbColumn = "name" });
            user.Fields.Add(new TextFieldDesign { Name = "Email", DbColumn = "email" });
            user.Fields.Add(new LoginAccountContractFieldDesign
            {
                Name = "LoginAccount", LoginName = "UserName", DisplayName = "Name",
                TwoFactorEmail = withTwoFactorEmail ? "Email" : "",
                DbColumnPasswordHash = withPassword ? "hash" : "", DbColumnPasswordSalt = withPassword ? "salt" : "",
                DbColumnTotpSecret = withTotp ? "totp_secret" : "", DbColumnTotpConfirmed = withTotp ? "totp_confirmed" : "", DbColumnTotpLastTimestep = withTotp ? "totp_last_timestep" : "",
            });
            d.AddModule(user);
            return d;
        }

        [SetUp]
        public async Task SetUp()
        {
            DbAccessor.ClearTableDefinitionCache();
            _dbFile = Path.Combine(Path.GetTempPath(), $"pw_login_test_{Guid.NewGuid():N}.db");
            _db = new DbAccessor([new DataSource { Name = Ds, DataSourceType = DataSourceType.SQLite, ConnectionString = $"Data Source={_dbFile}" }]);
            await _db.ExecuteAsync(Ds, "CREATE TABLE app_users (id TEXT PRIMARY KEY, user_name TEXT, name TEXT, email TEXT, hash TEXT, salt TEXT, totp_secret TEXT, totp_confirmed INTEGER, totp_last_timestep INTEGER)", new());
            var hashed = PasswordHashHelper.CreateHash("p@ss");
            await _db.ExecuteAsync(Ds, "INSERT INTO app_users (id, user_name, name, email, hash, salt) VALUES ('U1', 'taro', 'Taro', 'taro@example.com', @h, @s)",
                new() { { "@h", hashed.Hash }, { "@s", hashed.Salt } });
            _cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            _sent.Clear();
            _sendFails = false;
        }

        [TearDown]
        public async Task TearDown()
        {
            await _db.DisposeAsync();
            SqliteTestDb.Delete(_dbFile);
        }

        PasswordLogin Create(DesignData? design = null)
            => new(design ?? CreateDesign(), _db, _cache,
                new LoginLockoutSettings { MaxFailedAttempts = MaxFailed, LockoutMinutes = 5 }, new TotpLoginSettings { Issuer = "Test" }, new EmailOtpLoginSettings(),
                m =>
                {
                    if (_sendFails) return Task.FromResult(new MailSendResult { TotalCount = 1, Failures = { new MailSendFailure { To = m.To.FirstOrDefault() ?? "", Error = "down" } } });
                    _sent.Add(m);
                    return Task.FromResult(new MailSendResult { TotalCount = 1, SuccessCount = 1 });
                });

        static string CodeIn(MailMessage m) => Regex.Match(m.Body, @"\b(\d{6})\b").Groups[1].Value;

        static string CurrentTotpCode(string secret) => Totp.ComputeCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Totp.PeriodSeconds);

        [Test]
        public async Task 契約が無い_パスワードの列が無い_はNotAvailable()
        {
            var noContract = new DesignData();
            noContract.AppSettings.CurrentUserModuleDesignName = "Missing";
            Assert.That((await Create(noContract).VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.NotAvailable));
            Assert.That((await Create(CreateDesign(withPassword: false)).VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.NotAvailable));
        }

        [Test]
        public async Task 一致すればOkでアカウントとokの応答_補足は無い()
        {
            var result = await Create().VerifyAsync("taro", "p@ss", null);
            Assert.That(result.Status, Is.EqualTo(PasswordLoginStatus.Ok));
            Assert.That(result.Account!.UserId, Is.EqualTo("U1"));
            Assert.That(result.Account.DisplayName, Is.EqualTo("Taro"));
            Assert.That(((TotpLoginResult)result.Response!).Status, Is.EqualTo(TotpLoginStatus.Ok));
            Assert.That(result.Detail, Is.Null);
            Assert.That(result.IsDenied, Is.False);
        }

        [Test]
        public async Task 不一致はUnauthorizedで理由を出さず_続けばロックされ正しいパスワードも通らない()
        {
            var login = Create();
            for (var i = 0; i < MaxFailed; i++)
            {
                var result = await login.VerifyAsync("taro", "wrong", null);
                Assert.That(result.Status, Is.EqualTo(PasswordLoginStatus.Unauthorized));
                Assert.That(result.Account, Is.Null);
                Assert.That(result.Response, Is.Null);
                Assert.That(result.Detail, Is.Null, "不一致の理由は出さない (401 で拒否になる)");
                Assert.That(result.IsDenied, Is.False);
            }
            var locked = await login.VerifyAsync("taro", "p@ss", null);
            Assert.That(locked.Status, Is.EqualTo(PasswordLoginStatus.Locked));
            Assert.That(locked.Detail, Is.EqualTo("Locked"));
            Assert.That(locked.IsDenied, Is.True);
            Assert.That(locked.Account, Is.Null);
        }

        [Test]
        public async Task 存在しないログイン名は数えない()
        {
            var login = Create();
            for (var i = 0; i < MaxFailed + 2; i++)
            {
                Assert.That((await login.VerifyAsync("nobody", "wrong", null)).Status, Is.EqualTo(PasswordLoginStatus.Unauthorized), "ロックにはならない");
            }
            Assert.That((await login.VerifyAsync("", "wrong", null)).Status, Is.EqualTo(PasswordLoginStatus.Unauthorized));
        }

        [Test]
        public async Task 成功すると失敗の数は消える()
        {
            var login = Create();
            for (var i = 0; i < MaxFailed - 1; i++) await login.VerifyAsync("taro", "wrong", null);
            Assert.That((await login.VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.Ok));
            for (var i = 0; i < MaxFailed - 1; i++) Assert.That((await login.VerifyAsync("taro", "wrong", null)).Status, Is.EqualTo(PasswordLoginStatus.Unauthorized));
            Assert.That((await login.VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.Ok), "数え直されているのでまだロックされない");
        }

        [Test]
        public async Task 認証アプリ_登録とコード要求は数えず_コード不一致は拒否として数え_一致でOk()
        {
            var login = Create(CreateDesign(withTotp: true));

            //1 段階目 (コード無し): 未登録なので登録情報。何度繰り返してもロックされない
            TotpLoginResult setup = null!;
            for (var i = 0; i < MaxFailed + 2; i++)
            {
                var result = await login.VerifyAsync("taro", "p@ss", null);
                Assert.That(result.Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
                Assert.That(result.Account, Is.Null, "二要素認証の途中ではアカウントを出さない");
                Assert.That(result.Detail, Is.EqualTo("TwoFactor=setup"));
                Assert.That(result.IsDenied, Is.False);
                setup = (TotpLoginResult)result.Response!;
                Assert.That(setup.Status, Is.EqualTo(TotpLoginStatus.Setup));
                Assert.That(setup.Secret, Is.Not.Empty);
            }

            //コード不一致: 拒否として数える → MaxFailed 回でロック
            for (var i = 0; i < MaxFailed; i++)
            {
                var result = await login.VerifyAsync("taro", "p@ss", "000000" == CurrentTotpCode(setup.Secret!) ? "111111" : "000000");
                Assert.That(result.Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
                Assert.That(((TotpLoginResult)result.Response!).Status, Is.EqualTo(TotpLoginStatus.InvalidCode));
                Assert.That(result.Detail, Is.EqualTo("TwoFactor=invalid_code"));
                Assert.That(result.IsDenied, Is.True);
            }
            Assert.That((await login.VerifyAsync("taro", "p@ss", CurrentTotpCode(setup.Secret!))).Status, Is.EqualTo(PasswordLoginStatus.Locked), "正しいコードでもロック中");

            //ロックを解いて (新しいキャッシュ) 正しいコードで登録確定 → Ok
            _cache = new MemoryDistributedCache(Microsoft.Extensions.Options.Options.Create(new MemoryDistributedCacheOptions()));
            login = Create(CreateDesign(withTotp: true));
            var ok = await login.VerifyAsync("taro", "p@ss", CurrentTotpCode(setup.Secret!));
            Assert.That(ok.Status, Is.EqualTo(PasswordLoginStatus.Ok));
            Assert.That(ok.Account!.UserId, Is.EqualTo("U1"));

            //登録後の 1 段階目はコード要求
            var required = await login.VerifyAsync("taro", "p@ss", null);
            Assert.That(required.Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
            Assert.That(required.Detail, Is.EqualTo("TwoFactor=totp"));
            Assert.That(((TotpLoginResult)required.Response!).Status, Is.EqualTo(TotpLoginStatus.CodeRequired));
        }

        [Test]
        public async Task メール_コードを発行して送り_一致でOk_送れなければsend_failedで数えない()
        {
            var login = Create(CreateDesign(withTwoFactorEmail: true));

            var issued = await login.VerifyAsync("taro", "p@ss", null);
            Assert.That(issued.Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
            Assert.That(issued.Detail, Is.EqualTo("TwoFactor=email"));
            Assert.That(issued.IsDenied, Is.False);
            var response = (EmailOtpLoginResult)issued.Response!;
            Assert.That(response.Status, Is.EqualTo(EmailOtpLoginStatus.CodeRequired));
            Assert.That(response.MaskedEmail, Is.EqualTo("t***@example.com"));
            Assert.That(_sent, Has.Count.EqualTo(1));
            Assert.That(_sent[0].To, Is.EqualTo(new[] { "taro@example.com" }));

            var ok = await login.VerifyAsync("taro", "p@ss", CodeIn(_sent[0]));
            Assert.That(ok.Status, Is.EqualTo(PasswordLoginStatus.Ok));
            Assert.That(ok.Account!.UserId, Is.EqualTo("U1"));

            //送れなかった回は失敗に数えない
            _sendFails = true;
            for (var i = 0; i < MaxFailed + 2; i++)
            {
                var failed = await login.VerifyAsync("taro", "p@ss", null);
                Assert.That(failed.Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
                Assert.That(failed.Detail, Is.EqualTo("TwoFactor=send_failed"));
                Assert.That(failed.IsDenied, Is.False);
            }
            _sendFails = false;
            Assert.That((await login.VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.TwoFactor), "ロックされていない");
        }

        [Test]
        public async Task 照合中の例外は投げ直し_失敗に数えない()
        {
            //契約は TOTP の列を指すが表に無い: パスワードの照合は通り、その後の TOTP の読みで DB が例外になる
            await _db.ExecuteAsync(Ds, "ALTER TABLE app_users RENAME COLUMN totp_secret TO totp_secret_missing", new());
            DbAccessor.ClearTableDefinitionCache();
            var login = Create(CreateDesign(withTotp: true));
            for (var i = 0; i < MaxFailed + 2; i++)
            {
                Assert.CatchAsync<Exception>(() => login.VerifyAsync("taro", "p@ss", null));
            }

            //表を直すとロックされずに通る
            await _db.ExecuteAsync(Ds, "ALTER TABLE app_users RENAME COLUMN totp_secret_missing TO totp_secret", new());
            DbAccessor.ClearTableDefinitionCache();
            Assert.That((await login.VerifyAsync("taro", "p@ss", null)).Status, Is.EqualTo(PasswordLoginStatus.TwoFactor));
        }
    }
}
