using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Extras.Services;

namespace Extras.Server.Services
{
    public class CustomizedModuleDataIO : ModuleDataIO
    {
        readonly DesignData _designData;

        public CustomizedModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
            : base(designData, authenticationContext, dbAccess, temporaryFileManager)
        {
            _designData = designData;
        }

        protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            if (moduleDesign == null) throw LowCodeException.Create("invalid design");

            PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
            return await base.AddAsync(transactionId, moduleSubmitId, data);
        }

        protected async override Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            if (moduleDesign == null) throw LowCodeException.Create("invalid design");

            PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
            await base.UpdateAsync(transactionId, moduleSubmitId, data);
        }
        //メール送信履歴などシステムの記録を、操作ユーザーの書き込み権限に依存せず追加する内部経路。
        //クライアントから直接は呼ばれない(サーバー内部の記録専用)。戻り値は採番された Id (承認フローが使う)
        internal async Task<string> AddSystemRecordAsync(ModuleData data)
            => await AddAsync(Guid.NewGuid(), Guid.NewGuid(), data);

        //BulkMailFieldの送信結果サマリなど、既存レコードへのシステムの記録の書き戻し用内部経路。
        //data に含まれるフィールドだけが更新される
        internal async Task UpdateSystemRecordAsync(ModuleData data)
            => await UpdateAsync(Guid.NewGuid(), Guid.NewGuid(), data);

    }
}
