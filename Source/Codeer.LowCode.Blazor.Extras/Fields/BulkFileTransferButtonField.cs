using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.ScriptObjects;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class BulkFileTransferButtonField(BulkFileTransferButtonFieldDesign design)
        : FieldBase<BulkFileTransferButtonFieldDesign>(design)
    {
        bool _isTransferring;

        //値を持たないフィールド
        [ScriptHide]
        public override bool IsModified => false;
        [ScriptHide]
        public override FieldDataBase? GetData() => null;
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();
        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;
        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        /// <summary>ダウンロード/アップロード実行中か (ボタンの二重実行防止)。</summary>
        internal bool IsTransferring => _isTransferring;

        /// <summary>ダウンロードボタンを表示するか。</summary>
        internal bool CanDownload => Design.CanBulkDataDownload;

        /// <summary>アップロードボタンを表示するか。権限による出し分けはフィールドでは行わない (サーバー側の検証と、必要ならデザイン側の可視制御に委ねる)。</summary>
        internal bool CanUpload => Design.CanBulkDataUpdate;

        /// <summary>条件に一致するデータを一括ダウンロードする。ファイル形式は対象モジュールの定義 (Csv/FileColumnMappingField) に従う。</summary>
        internal async Task DownloadAsync()
        {
            if (_isTransferring) return;

            _isTransferring = true;
            NotifyStateChanged();
            try
            {
                await BulkFileDownload.DownloadAsync(Services, GetDownloadSearchCondition());
            }
            finally
            {
                _isTransferring = false;
                NotifyStateChanged();
            }
        }

        /// <summary>アップロードされたファイルで一括更新する (コンポーネントのファイル選択から呼ばれる)。</summary>
        internal async Task UploadAsync(Stream stream)
        {
            if (Services.AppInfoService.IsDesignMode) return;
            if (_isTransferring) return;

            var moduleName = GetTargetModuleName();
            if (string.IsNullOrEmpty(moduleName)) return;

            _isTransferring = true;
            NotifyStateChanged();
            try
            {
                var ret = await Services.ModuleDataService.SubmitByFileAsync(moduleName, new StreamContent(stream));
                if (ret == null) return;
                foreach (var x in ret)
                {
                    if (!string.IsNullOrEmpty(x.ExceptionMessage))
                    {
                        await Services.Logger.Error(x.ExceptionMessage);
                        return;
                    }
                }

                await ReloadConditionSourceAsync();
                await Module!.ExecuteScriptAsync(Design.OnUploaded);
                await Services.UIService.NotifySuccess(Properties.Resources.SuccessSubmit);
            }
            finally
            {
                _isTransferring = false;
                NotifyStateChanged();
            }
        }

        /// <summary>条件ソース (検索フィールド/リストフィールド) から一括ダウンロードの検索条件を作る。</summary>
        internal SearchCondition? GetDownloadSearchCondition()
        {
            if (!string.IsNullOrEmpty(Design.SearchFieldName))
            {
                if (Module?.GetField(Design.SearchFieldName) is not SearchField searchField) return null;
                return BulkFileDownload.FromSearchField(searchField);
            }
            if (!string.IsNullOrEmpty(Design.ListFieldName))
            {
                if (Module?.GetField(Design.ListFieldName) is not ListField listField) return null;
                return BulkFileDownload.FromListField(listField);
            }
            return null;
        }

        /// <summary>一括ダウンロード/一括更新の対象モジュール名。</summary>
        internal string? GetTargetModuleName()
        {
            if (!string.IsNullOrEmpty(Design.SearchFieldName))
            {
                if (Module?.GetField(Design.SearchFieldName) is not SearchField searchField) return null;
                var moduleName = searchField.Condition?.ModuleName;
                if (!string.IsNullOrEmpty(moduleName)) return moduleName;
                return (Module?.GetField(searchField.Design.ResultsViewFieldName) as ISearchResultsViewField)?.ModuleName;
            }
            if (!string.IsNullOrEmpty(Design.ListFieldName))
            {
                return (Module?.GetField(Design.ListFieldName) as ListField)?.ModuleName;
            }
            return null;
        }

        async Task ReloadConditionSourceAsync()
        {
            if (!string.IsNullOrEmpty(Design.SearchFieldName))
            {
                if (Module?.GetField(Design.SearchFieldName) is not SearchField searchField) return;
                if (Module?.GetField(searchField.Design.ResultsViewFieldName) is ISearchResultsViewField resultsView)
                {
                    await resultsView.ReloadAsync();
                }
                return;
            }
            if (!string.IsNullOrEmpty(Design.ListFieldName))
            {
                if (Module?.GetField(Design.ListFieldName) is ListField listField)
                {
                    await listField.ReloadAsync();
                }
            }
        }
    }
}
