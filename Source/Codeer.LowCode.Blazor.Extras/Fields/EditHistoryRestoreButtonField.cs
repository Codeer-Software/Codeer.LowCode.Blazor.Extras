using Codeer.LowCode.Blazor.Components.Dialog.BootstrapButtons;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;
using R = Codeer.LowCode.Blazor.Extras.Properties.Resources;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 履歴モジュールの詳細に置く「このレコードを復活」ボタンのランタイム。
    /// 自モジュール (履歴) の契約で ChangeType / ModuleName / DataId / Snapshot を読み、削除の版だけ復活できる。
    /// </summary>
    public class EditHistoryRestoreButtonField : FieldBase<EditHistoryRestoreButtonFieldDesign>
    {
        bool _isBusy;

        public EditHistoryRestoreButtonField(EditHistoryRestoreButtonFieldDesign design) : base(design) { }

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase) => await Task.CompletedTask;

        public bool IsBusy => _isBusy;

        public string ButtonText => string.IsNullOrEmpty(Design.Text) ? R.EditHistoryRestoreButton_DefaultText : Design.Text;

        EditHistoryContractFieldDesign Names => EditHistoryContracts.Contract(Module.Design) ?? new();

        string GetText(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            var data = Module.GetField(fieldName)?.GetData();
            return (data as ValueFieldDataBase<string>)?.Value ?? string.Empty;
        }

        /// <summary>削除の版で、対象モジュールがあるときだけ復活できる。</summary>
        [ScriptHide]
        public bool CanRestore
            => !Services.AppInfoService.IsDesignMode && !Module.IsNewData && IsEnabled &&
               GetText(Names.ChangeType) == EditHistoryChangeType.Delete.ToString() &&
               Services.AppInfoService.GetDesignData().Modules.Find(GetText(Names.ModuleName)) != null;

        /// <summary>削除されたレコードを復活させる。論理削除なら Id を保って戻し、物理削除なら新しいレコードとして作る。</summary>
        [ScriptName("Restore")]
        public async Task<bool> RestoreAsync()
        {
            if (_isBusy || !CanRestore) return false;
            var names = Names;
            var moduleName = GetText(names.ModuleName);
            var dataId = GetText(names.DataId);
            var target = Services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            var snapshot = EditHistorySnapshot.Deserialize(GetText(names.Snapshot));
            if (target == null || snapshot == null) return false;

            var answer = await Services.UIService.ShowMessageBox(ButtonText,
                string.Format(R.EditHistoryRestoreRecordConfirmFormat, moduleName, dataId),
                [new PrimaryButton(R.EditHistoryRestore, true), new SecondaryOutlineButton(R.EditHistoryCancel)]);
            if (answer != R.EditHistoryRestore) return false;

            _isBusy = true;
            NotifyStateChanged();
            try
            {
                var restoredId = EditHistoryContracts.IsLogicalDeleteModule(target)
                    ? await UndeleteAsync(target, dataId, snapshot)
                    : await CreateAsync(target, snapshot);
                if (restoredId == null) return false;

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

        //論理削除: 削除の版のスナップショットにある Id (レコード + 従属レコード・孫) を Undelete で戻す
        async Task<string?> UndeleteAsync(ModuleDesign target, string dataId, ModuleData snapshot)
        {
            var undelete = new EditHistoryUndeleteData();
            Collect(target, snapshot, undelete.Targets, new HashSet<string> { target.Name });
            if (!undelete.Targets.Any(e => e.ModuleName == target.Name && e.Id == dataId))
                undelete.Targets.Insert(0, new EditHistoryUndeleteTarget { ModuleName = target.Name, Id = dataId });

            var submit = new ModuleSubmitData { ModuleName = target.Name, Id = dataId };
            submit.ExtendedData.Add(undelete);
            var results = await Services.ModuleDataService.SubmitAsync([submit]);
            var error = results?.FirstOrDefault(e => !string.IsNullOrEmpty(e.ExceptionMessage))?.ExceptionMessage;
            if (results == null || error != null)
            {
                await Services.UIService.NotifyError(string.Format(R.EditHistoryRestoreRecordFailedFormat, error ?? string.Empty));
                return null;
            }
            return dataId;
        }

        void Collect(ModuleDesign design, ModuleData data, List<EditHistoryUndeleteTarget> targets, HashSet<string> visiting)
        {
            var id = EditHistorySnapshot.GetId(data);
            if (id.Length != 0 && EditHistoryContracts.IsLogicalDeleteModule(design))
                targets.Add(new EditHistoryUndeleteTarget { ModuleName = design.Name, Id = id });

            var designData = Services.AppInfoService.GetDesignData();
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(design))
            {
                var childDesign = designData.Modules.Find(owned.Condition.ModuleName);
                if (childDesign == null || visiting.Contains(childDesign.Name)) continue;
                if (!data.Fields.TryGetValue(owned.Name, out var listData) || listData is not ListFieldData rows) continue;
                var childVisiting = new HashSet<string>(visiting) { childDesign.Name };
                foreach (var row in rows.Children) Collect(childDesign, row, targets, childVisiting);
            }
        }

        //物理削除: スナップショットから新しいレコードを組み立てて保存する (Id は振り直し・明細も新しい行)
        async Task<string?> CreateAsync(ModuleDesign target, ModuleData snapshot)
        {
            var module = await ModuleCreationService.CreateModuleAsync(Services, new ModuleData { Name = target.Name }, ModuleLayoutType.Detail);
            await EditHistoryRestorer.ApplyAsync(module, snapshot, null);
            if (await module.SubmitAsync() != true)
            {
                await Services.UIService.NotifyError(string.Format(R.EditHistoryRestoreRecordFailedFormat, string.Empty));
                return null;
            }
            return module.GetIdText();
        }
    }
}
