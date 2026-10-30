using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Server.Auth
{
    /// <summary>
    /// 行を書く直前にパスワードの平文をハッシュ + ソルトに置き換える ModuleDataIO (PasswordHashField / LoginAccountContractField が指す PasswordField。
    /// 置き換えの内容は <see cref="PasswordHashHelper.ApplyPasswordHash"/>)。ホストの CustomizedModuleDataIO はこれを継承する。
    /// 本体の行書き込み (AddAsync / UpdateAsync / BulkAddAsync) を override しているので、保存 (Submit) だけでなくシステム権限の内部書き込み (ModuleDataIOSystemAccess) も通り、
    /// フィールドの書き込み権限の検査の後に入る (ハッシュ列の書き込み権限は見ない。ユーザーが書くのは PasswordField)。
    /// </summary>
    public class PasswordHashingModuleDataIO : ModuleDataIO
    {
        readonly DesignData _designData;

        public PasswordHashingModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
            : base(designData, authenticationContext, dbAccess, temporaryFileManager)
            => _designData = designData;

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

        protected override Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            ApplyPasswordHash(data);
            return base.UpdateAsync(transactionId, moduleSubmitId, data);
        }

        void ApplyPasswordHash(ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            if (moduleDesign != null) PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
        }
    }
}
