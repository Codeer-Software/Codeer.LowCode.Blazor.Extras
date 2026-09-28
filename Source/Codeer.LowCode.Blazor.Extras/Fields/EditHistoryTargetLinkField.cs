using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.EditHistory;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using R = Codeer.LowCode.Blazor.Extras.Properties.Resources;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 履歴行の対象レコードを開くリンクのランタイム。自モジュール (履歴) の契約で ModuleName / DataId / ChangeType を読む。
    /// </summary>
    public class EditHistoryTargetLinkField : FieldBase<EditHistoryTargetLinkFieldDesign>
    {
        public EditHistoryTargetLinkField(EditHistoryTargetLinkFieldDesign design) : base(design) { }

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

        public string LinkText => string.IsNullOrEmpty(Design.Text) ? R.EditHistoryTargetLink_DefaultText : Design.Text;

        EditHistoryContractFieldDesign Names => EditHistoryContracts.Contract(Module.Design) ?? new();

        string GetText(string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            var data = Module.GetField(fieldName)?.GetData();
            return (data as ValueFieldDataBase<string>)?.Value ?? string.Empty;
        }

        /// <summary>対象レコードの詳細の URL。削除の版 (レコードはもう開けない)・対象モジュールが無い・未保存の行では空。</summary>
        [ScriptHide]
        public string TargetUrl
        {
            get
            {
                if (Services.AppInfoService.IsDesignMode || Module.IsNewData) return string.Empty;
                var names = Names;
                if (GetText(names.ChangeType) == EditHistoryChangeType.Delete.ToString()) return string.Empty;
                var moduleName = GetText(names.ModuleName);
                var dataId = GetText(names.DataId);
                if (moduleName.Length == 0 || dataId.Length == 0) return string.Empty;
                if (Services.AppInfoService.GetDesignData().Modules.Find(moduleName) == null) return string.Empty;
                return Services.NavigationService.GetModuleDataUrl(moduleName, dataId);
            }
        }

        /// <summary>対象レコードの詳細へ遷移する (開けない行では何もしない)。</summary>
        [ScriptName("Open")]
        public void Open()
        {
            var url = TargetUrl;
            if (url.Length != 0) Services.NavigationService.NavigateTo(url);
        }
    }
}
