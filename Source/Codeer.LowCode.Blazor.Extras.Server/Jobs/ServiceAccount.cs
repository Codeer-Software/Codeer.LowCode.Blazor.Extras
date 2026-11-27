using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Server.Auth;

namespace Codeer.LowCode.Blazor.Extras.Server.Jobs
{
    /// <summary>
    /// ジョブが名前で指すサービスアカウント (ジョブを動かすユーザー) の 1 つ分。ホストが設定 (appsettings の ServiceAccounts) から読む。
    /// </summary>
    public class ServiceAccount
    {
        /// <summary>
        /// サービスアカウントの名前。ジョブはこの名前で指す。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// そのユーザーのログイン ID (ログイン画面で入力するもの)。
        /// </summary>
        public string User { get; set; } = string.Empty;

        /// <summary>
        /// サービスアカウントの名前からユーザーの Id を引く (accounts で名前 → ログイン ID、ユーザーモジュールの LoginAccountContractField でログイン ID → ユーザー)。
        /// 名前が accounts に無い・ユーザーが見つからない・停止中のユーザーなら例外 (ジョブは失敗)。
        /// ユーザーモジュールに LoginAccountContractField が無いアプリでは、User に書いた値をそのままユーザーの Id として返す。
        /// </summary>
        public static async Task<string> FindUserIdAsync(IEnumerable<ServiceAccount> accounts, string name, DesignData designData, IDbAccessor db)
        {
            var loginName = accounts.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))?.User;
            if (string.IsNullOrEmpty(loginName)) throw new InvalidOperationException($"ServiceAccounts に '{name}' がありません");

            var store = LoginAccountStore.Create(designData, db);
            if (store == null) return loginName;
            return (await store.FindByLoginNameAsync(loginName))?.UserId
                ?? throw new InvalidOperationException($"ServiceAccounts の '{name}' の User が見つかりません");
        }
    }
}
