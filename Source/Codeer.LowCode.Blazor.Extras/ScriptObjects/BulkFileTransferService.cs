using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// 一括ダウンロード (一覧ページ/BulkFileTransferButtonField と同じ list_file) をスクリプトから実行するサービス。
    /// ファイル形式は対象モジュールの CsvFileFormatField / FileColumnMappingField の定義に従う。
    /// このサービスを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    public class BulkFileTransferService
    {
        [ScriptInject]
        public Codeer.LowCode.Blazor.RequestInterfaces.Services? Services { get; set; }

        /// <summary>ModuleSearcher の条件で一括ダウンロードする。</summary>
        [ScriptName("Download")]
        public async Task DownloadAsync(ModuleSearcher searcher)
            => await BulkFileDownload.DownloadAsync(Services, searcher.GetSearchCondition());

        /// <summary>検索フィールドの現在の検索条件で一括ダウンロードする。</summary>
        [ScriptName("Download")]
        public async Task DownloadAsync(SearchField searchField)
            => await BulkFileDownload.DownloadAsync(Services, BulkFileDownload.FromSearchField(searchField));

        /// <summary>リストフィールドの表示中の検索条件で一括ダウンロードする (一覧レイアウトの列・ページサイズには縛られず全列/全件)。</summary>
        [ScriptName("Download")]
        public async Task DownloadAsync(ListField listField)
            => await BulkFileDownload.DownloadAsync(Services, BulkFileDownload.FromListField(listField));
    }

    /// <summary>一括ダウンロードの共有ロジック (BulkFileTransferButtonField と BulkFileTransferService が使う)。</summary>
    internal static class BulkFileDownload
    {
        /// <summary>検索フィールドの現在の検索条件 (未検索なら結果表示フィールドから対象モジュールを補完した全件条件)。</summary>
        internal static SearchCondition FromSearchField(SearchField searchField)
        {
            var condition = searchField.Condition?.JsonClone() ?? new SearchCondition();
            if (string.IsNullOrEmpty(condition.ModuleName))
            {
                condition.ModuleName = (searchField.Module?.GetField(searchField.Design.ResultsViewFieldName)
                    as ISearchResultsViewField)?.ModuleName ?? string.Empty;
            }
            return condition;
        }

        /// <summary>リストフィールドの表示中の検索条件 (一覧レイアウトの列・ページサイズには縛られず全列/全件)。</summary>
        internal static SearchCondition? FromListField(ListField listField)
        {
            var condition = listField.GetSearchCondition();
            if (condition != null)
            {
                condition.SelectFields = new();
                condition.LimitCount = null;
            }
            return condition;
        }

        /// <summary>条件に一致するデータを一括ダウンロードする (ファイル名は {モジュール名}.{IBulkFileTransferFieldDesignの拡張子 or xlsx})。</summary>
        internal static async Task DownloadAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services? services, SearchCondition? condition)
        {
            if (services == null) return;
            if (services.AppInfoService.IsDesignMode) return;
            if (condition == null || string.IsNullOrEmpty(condition.ModuleName)) return;

            var stream = await services.ModuleDataService.GetListFileAsync(condition);
            if (stream == null) return;
            stream.Position = 0;
            await services.UIService.DownloadFile(stream, GetDownloadFileName(services, condition.ModuleName));
        }

        static string GetDownloadFileName(Codeer.LowCode.Blazor.RequestInterfaces.Services services, string moduleName)
        {
            //一覧ページの一括ダウンロードと同じ規約 (IBulkFileTransferFieldDesign 未定義なら xlsx)
            var moduleDesign = services.AppInfoService.GetDesignData().Modules.Find(moduleName);
            var extension = moduleDesign?.Fields.OfType<IBulkFileTransferFieldDesign>().FirstOrDefault()?.Extension;
            if (string.IsNullOrEmpty(extension)) extension = "xlsx";
            return $"{moduleName}.{extension}";
        }
    }
}
