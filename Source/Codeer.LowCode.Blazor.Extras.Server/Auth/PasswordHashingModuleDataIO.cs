using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 行を書く直前にパスワードの平文をハッシュ + ソルトに置き換える ModuleDataIO (PasswordHashField / LoginAccountContractField が指す PasswordField。
    /// 置き換えの内容は <see cref="PasswordHashHelper.ApplyPasswordHash"/>)。ホストの CustomizedModuleDataIO はこれを継承する。
    /// 本体の行書き込み (AddAsync / UpdateAsync / BulkAddAsync) を override しているので、保存 (Submit) だけでなくシステム権限の内部書き込み (ModuleDataIOSystemAccess) も通り、
    /// フィールドの書き込み権限の検査の後に入る (ハッシュ列の書き込み権限は見ない。ユーザーが書くのは PasswordField)。
    ///
    /// 契約にセッションの印の列 (DbColumnSessionStamp) があれば、ログイン済み Cookie の失効もここで行う (行の UPDATE と同じトランザクション):
    /// パスワードが変わった行 (契約の PasswordField = 契約のデータに新しい印が入る / 同じ表を書く別モジュールの PasswordHashField = 印の列を直接書く) と、
    /// ユーザーモジュールの保存で有効フラグ (契約の IsActive) が偽の行は印を新しくする。表示名など他の項目だけの保存では変えない。
    /// </summary>
    public class PasswordHashingModuleDataIO : ModuleDataIO
    {
        readonly DesignData _designData;
        readonly IDbAccessor _dbAccess;

        public PasswordHashingModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
            : base(designData, authenticationContext, dbAccess, temporaryFileManager)
        {
            _designData = designData;
            _dbAccess = dbAccess;
        }

        protected override Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            ApplyPasswordHash(data);
            return base.AddAsync(transactionId, moduleSubmitId, data);
        }

        //一括 INSERT (大量取込) は行ごとの AddAsync を通らない
        protected override Task BulkAddAsync(Guid transactionId, List<ModuleData> datas)
        {
            foreach (var data in datas) ApplyPasswordHash(data);
            return base.BulkAddAsync(transactionId, datas);
        }

        protected override async Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            var passwordWritten = ApplyPasswordHash(data);
            await base.UpdateAsync(transactionId, moduleSubmitId, data);
            await RevokeSessionsIfNeededAsync(data, passwordWritten);
        }

        bool ApplyPasswordHash(ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            return moduleDesign != null && PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
        }

        //印を新しくする条件: 同じ表 (ユーザーモジュールの表) の行で、パスワードを書き換えた (契約のデータに印が入っていればその UPDATE で済んでいる) か、
        //ユーザーモジュールの保存で有効フラグが偽 (停止。有効に戻しても停止前の Cookie が生き返らないように)
        async Task RevokeSessionsIfNeededAsync(ModuleData data, bool passwordWritten)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            var userModule = _designData.Modules.Find(_designData.AppSettings.CurrentUserModuleDesignName);
            if (moduleDesign == null || userModule == null) return;
            if (moduleDesign.DbTable != userModule.DbTable || moduleDesign.DataSourceName != userModule.DataSourceName) return;
            var contract = userModule.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            if (contract == null || !contract.HasSessionStamp) return;

            var stampAlreadyWritten = data.Fields.TryGetValue(contract.Name, out var contractData) && contractData is LoginAccountContractFieldData { SessionStamp: not null };
            var inactive = moduleDesign.Name == userModule.Name && IsInactive(contract.IsActive, data);
            if (!((passwordWritten && !stampAlreadyWritten) || inactive)) return;

            var id = data.Fields.TryGetValue(SystemFieldNames.Id, out var idData) ? (idData as IdFieldData)?.Value?.ToString() : null;
            if (string.IsNullOrEmpty(id)) return;

            await new LoginAccountStore(userModule, contract, _dbAccess).RevokeSessionsAsync(id);
        }

        //保存に有効フラグのフィールドが載っていて、その値が偽か (載っていなければ変えていない = 判定しない)
        static bool IsInactive(string isActiveFieldName, ModuleData data)
        {
            if (string.IsNullOrEmpty(isActiveFieldName) || !data.Fields.TryGetValue(isActiveFieldName, out var fieldData)) return false;
            var value = fieldData switch
            {
                ValueFieldDataBase<bool?> b => (object?)b.Value,
                ValueFieldDataBase<decimal?> n => n.Value,
                ValueFieldDataBase<string> s => s.Value,
                _ => null,
            };
            return !LoginAccountStore.IsActiveValue(value);
        }
    }
}
