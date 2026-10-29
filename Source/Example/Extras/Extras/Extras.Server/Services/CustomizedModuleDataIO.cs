using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Extras.Server.AuditLog;
using Codeer.LowCode.Blazor.Extras.Server.EditHistory;
using Codeer.LowCode.Blazor.Extras.Services;
using Extras.Server.AI;

namespace Extras.Server.Services
{
    public class CustomizedModuleDataIO : ModuleDataIO
    {
        readonly DesignData _designData;

        public CustomizedModuleDataIO(DesignData designData, IAuthenticationContext authenticationContext, IDbAccessor dbAccess, ITemporaryFileManager temporaryFileManager)
            : base(designData, authenticationContext, dbAccess, temporaryFileManager)
        {
            _designData = designData;
            //監査ログ: 保存の対象 (行ごとの Add / Update / Delete と Id・件数) を記録する。画面の保存・ファイル取込・スクリプトの一括保存はどれもここを通る。
            //監査ログのテーブルへのモジュールの保存は拒否する (追記専用。閲覧用のモジュールを作るのは可)。
            //保存の最終結果を記録するので、他のインターセプタより先 (外側) に登録する
            AddInterceptor(new AuditIOInterceptor(designData, SystemConfig.Instance.AuditLog.Database));
            //編集履歴: EditHistoryField を置いたモジュールの保存ごとに履歴モジュールへスナップショットを書き、履歴モジュールを読むときは Snapshot を読む人の権限に落とす (結線はこの 1 行)
            AddInterceptor(new EditHistoryRecorder(designData));
            //意味検索 (SemanticSearchField): 保存した行から索引 (文章と埋め込み) を作る・検索欄の文章を埋め込みにしてから検索する
            AddInterceptor(SemanticSearchIndex.Service.Interceptor);
        }


        protected override async Task<string> AddAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            if (moduleDesign == null) throw LowCodeException.Create("invalid design");

            PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
            return await base.AddAsync(transactionId, moduleSubmitId, data);
        }

        //一括INSERT (大量取込) は行ごとの AddAsync を通らないため、同じ加工をこちらでも行う
        protected override async Task BulkAddAsync(Guid transactionId, List<ModuleData> datas)
        {
            var moduleDesign = _designData.Modules.Find(datas.FirstOrDefault()?.Name ?? string.Empty);
            if (moduleDesign == null) throw LowCodeException.Create("invalid design");

            foreach (var data in datas) PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
            await base.BulkAddAsync(transactionId, datas);
        }

        protected async override Task UpdateAsync(Guid transactionId, Guid moduleSubmitId, ModuleData data)
        {
            var moduleDesign = _designData.Modules.Find(data.Name);
            if (moduleDesign == null) throw LowCodeException.Create("invalid design");

            PasswordHashHelper.ApplyPasswordHash(moduleDesign, data);
            await base.UpdateAsync(transactionId, moduleSubmitId, data);
        }

    }
}
