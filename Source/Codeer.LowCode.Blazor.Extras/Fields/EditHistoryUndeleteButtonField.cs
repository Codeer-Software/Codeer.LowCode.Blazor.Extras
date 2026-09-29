using Codeer.LowCode.Blazor.Components.Dialog.BootstrapButtons;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using R = Codeer.LowCode.Blazor.Extras.Properties.Resources;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 履歴モジュールの詳細に置く「このレコードを復活」ボタンのランタイム。
    /// 自モジュール (履歴) の契約で ChangeType / ModuleName / DataId を読み、削除の版だけ復活できる。
    /// 復活はサーバー (EditHistoryRecorder) が行う: クライアントは履歴行の Id だけを送り、サーバーがその版のスナップショットから
    /// レコード全体を戻す (論理削除は Id を保って取り消し、物理削除は作り直し)。権限は削除の逆 (CanDelete と UserWrite 条件、行の条件)。
    /// </summary>
    public class EditHistoryUndeleteButtonField : FieldBase<EditHistoryUndeleteButtonFieldDesign>
    {
        bool _isBusy;
        //この版がそのレコードの最新の版か (復活済み・作り直し済みのレコードの古い削除の版からは復活できない。サーバーも同じ判定をする)
        bool _isLatestVersion;

        public EditHistoryUndeleteButtonField(EditHistoryUndeleteButtonFieldDesign design) : base(design) { }

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _isLatestVersion = false;
            if (Services.AppInfoService.IsDesignMode || Module.IsNewData || ModuleLayoutType != ModuleLayoutType.Detail) return;
            if (GetText(Names.ChangeType) != EditHistoryChangeType.Delete.ToString()) return;
            _isLatestVersion = await IsLatestVersionAsync();
        }

        //同じレコード (ModuleName / DataId) の最新の版 (閲覧側 EditHistoryField と同じ並び) がこの行か
        async Task<bool> IsLatestVersionAsync()
        {
            var names = Names;
            var condition = EditHistoryContracts.VersionsCondition(Module.Design.Name, names, GetText(names.ModuleName), GetText(names.DataId), 1, SystemFieldNames.Id);
            var page = (await Services.ModuleDataService.GetListAsync([new GetListRequest { Condition = condition, PageIndex = 0 }])).FirstOrDefault();
            var latest = page?.Items.FirstOrDefault();
            return latest != null && EditHistorySnapshot.GetId(latest) == Module.GetIdText();
        }

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        public bool IsBusy => _isBusy;

        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? R.EditHistoryUndeleteButton_DefaultText : Design.Text;

        EditHistoryContractFieldDesign Names => EditHistoryContracts.Contract(Module.Design) ?? new();

        string GetText(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            var data = Module.GetField(fieldName)?.GetData();
            return (data as ValueFieldDataBase<string>)?.Value ?? string.Empty;
        }

        /// <summary>
        /// 削除の版で、それがそのレコードの最新の版で、対象モジュールがあり、このユーザーがそのモジュールで削除 (= 復活) できるときだけ出す。
        /// 行の条件はサーバーが見る。
        /// </summary>
        internal bool CanUndelete
        {
            get
            {
                if (Services.AppInfoService.IsDesignMode || Module.IsNewData || !IsEnabled || !_isLatestVersion) return false;
                if (GetText(Names.ChangeType) != EditHistoryChangeType.Delete.ToString()) return false;
                var target = Services.AppInfoService.GetDesignData().Modules.Find(GetText(Names.ModuleName));
                return target != null && target.CanUndeleteByUser(Services);
            }
        }

        /// <summary>削除されたレコードを復活させる。復活後はそのレコードの詳細に遷移する (物理削除は作り直した新しい Id)。</summary>
        [ScriptName("Undelete")]
        public async Task<bool> UndeleteAsync()
        {
            if (_isBusy || !CanUndelete) return false;
            var names = Names;
            var moduleName = GetText(names.ModuleName);
            var dataId = GetText(names.DataId);
            var target = Services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            if (target == null) return false;

            var answer = await Services.UIService.ShowMessageBox(ButtonText,
                string.Format(R.EditHistoryRestoreRecordConfirmFormat, moduleName, dataId),
                [new PrimaryButton(R.EditHistoryRestore, true), new SecondaryOutlineButton(R.EditHistoryCancel)]);
            if (answer != R.EditHistoryRestore) return false;

            _isBusy = true;
            NotifyStateChanged();
            try
            {
                //Add / Update 無しの送信に「この履歴行から復活」を同梱する。サーバーが版のスナップショットから戻し、結果の DestinationId が戻ったレコードの Id
                var submit = new ModuleSubmitData { ModuleName = target.Name, Id = dataId };
                submit.ExtendedData.Add(new EditHistoryUndeleteData
                {
                    HistoryModuleName = Module.Design.Name, HistoryRowId = Module.GetIdText(), RestoreWholeRecord = true,
                });
                var results = await Services.ModuleDataService.SubmitAsync([submit]);
                var error = results?.FirstOrDefault(e => !string.IsNullOrEmpty(e.ExceptionMessage))?.ExceptionMessage;
                if (results == null || error != null)
                {
                    await Services.UIService.NotifyError(string.Format(R.EditHistoryRestoreRecordFailedFormat, error ?? string.Empty));
                    return false;
                }
                var restoredId = results.FirstOrDefault()?.DestinationId;
                if (string.IsNullOrEmpty(restoredId)) restoredId = dataId;

                await Services.UIService.NotifySuccess(R.EditHistoryRestoreRecordDone);
                Services.NavigationService.NavigateTo(Services.NavigationService.GetModuleDataUrl(target.Name, restoredId));
                return true;
            }
            finally
            {
                _isBusy = false;
                NotifyStateChanged();
            }
        }
    }
}
