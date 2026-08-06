using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Microsoft.Extensions.DependencyInjection;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Codeer.LowCode.Blazor.Script.Internal.ScriptServices;

namespace Codeer.LowCode.Blazor.Extras.ScriptObjects
{
    /// <summary>
    /// 一括ダウンロード (一覧ページ/BulkFileTransferButtonField と同じ list_file) と一括保存をスクリプトから実行するサービス。
    /// ファイル形式は対象モジュールの CsvFileFormatField / FileColumnMappingField の定義に従う。
    /// このサービスを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    public class BulkFileTransferService
    {
        /// <summary>
        /// Download(List&lt;Module&gt;) が使うサーバーエンドポイント。URL はアプリ (Controller を持つ側) の
        /// 持ち物なのでアプリの初期化 (テンプレートの ServiceInitializer) で設定する。未設定なら何もしない。
        /// </summary>
        [ScriptHide]
        public static string ListFileByDataEndPoint { get; set; } = string.Empty;

        /// <summary>
        /// Submit(List&lt;Module&gt;) が使うサーバーエンドポイント。URL はアプリ (Controller を持つ側) の
        /// 持ち物なのでアプリの初期化 (テンプレートの ServiceInitializer) で設定する。未設定なら何もしない。
        /// </summary>
        [ScriptHide]
        public static string BulkSubmitEndPoint { get; set; } = string.Empty;

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

        /// <summary>
        /// 加工済みのモジュール列をそのまま一括ダウンロードする (検索せずクライアントのデータをファイル化する)。
        /// スクリプトで行を変換・絞り込みしてから出力する用途 (形式は対象モジュールの Csv/列マッピング定義に従う)。
        /// </summary>
        [ScriptName("Download")]
        public async Task DownloadAsync(List<Module> modules)
            => await BulkFileDownload.DownloadByDataAsync(Services, modules);

        /// <summary>
        /// 加工済みのモジュール列を一括保存する (1トランザクション。ファイル取込と同じく Id の一致で追加/更新を判定し、
        /// 新規行がまとまっていれば multi-row INSERT で高速に挿入される)。戻り値: true=保存成功。
        /// 保存した新規行の採番Id (テンポラリIdの解決) は返らないため、
        /// 保存後もモジュールを使い続ける (編集して再保存する) 用途には Module.Submit を使うこと。
        /// </summary>
        [ScriptName("Submit")]
        public async Task<bool> SubmitAsync(List<Module> modules)
        {
            if (Services == null) return false;
            if (string.IsNullOrEmpty(BulkSubmitEndPoint)) return false;
            if (Services.AppInfoService.IsDesignMode) return false;
            if (modules == null || modules.Count == 0) return false;
            var http = Services.Provider.GetService<Codeer.LowCode.Blazor.Extras.Services.IHttpService>();
            if (http == null) return false;

            var moduleName = modules[0].Design.Name;
            var json = JsonConverterEx.SerializeObject(modules.Select(e => e.GetData()).ToList());
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostContent($"{BulkSubmitEndPoint}?moduleName={moduleName}", content);
            if (response?.IsSuccessStatusCode != true) return false;

            var results = JsonConverterEx.DeserializeObject<List<Codeer.LowCode.Blazor.DataIO.ModuleSubmitResult>>(
                await response.Content.ReadAsStringAsync()) ?? new();
            foreach (var e in results)
            {
                if (!string.IsNullOrEmpty(e.ExceptionMessage))
                {
                    await Services.Logger.Error(e.ExceptionMessage);
                    return false;
                }
            }
            return true;
        }
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

        /// <summary>加工済みモジュール列の一括ダウンロード (検索せずクライアントのデータをそのままファイル化)。</summary>
        internal static async Task DownloadByDataAsync(Codeer.LowCode.Blazor.RequestInterfaces.Services? services, List<Module>? modules)
        {
            if (services == null) return;
            if (string.IsNullOrEmpty(BulkFileTransferService.ListFileByDataEndPoint)) return;
            if (services.AppInfoService.IsDesignMode) return;
            if (modules == null || modules.Count == 0) return;
            var http = services.Provider.GetService<Codeer.LowCode.Blazor.Extras.Services.IHttpService>();
            if (http == null) return;

            var moduleName = modules[0].Design.Name;
            var json = JsonConverterEx.SerializeObject(modules.Select(e => e.GetData()).ToList());
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await http.PostContent($"{BulkFileTransferService.ListFileByDataEndPoint}?moduleName={moduleName}", content);
            if (response?.IsSuccessStatusCode != true) return;

            var stream = new MemoryStream();
            await (await response.Content.ReadAsStreamAsync()).CopyToAsync(stream);
            stream.Position = 0;
            await services.UIService.DownloadFile(stream, GetDownloadFileName(services, moduleName));
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
