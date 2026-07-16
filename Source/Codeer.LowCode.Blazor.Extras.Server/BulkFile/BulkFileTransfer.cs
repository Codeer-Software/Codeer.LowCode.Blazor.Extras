using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新 (list_file / submit_by_file) のサーバー処理本体。
    /// テンプレートの ModuleDataController から移譲される。
    /// モジュールデザインの <see cref="CsvFileTransferFieldDesign"/> の有無で CSV / Excel を分岐する
    /// (クライアントも同じデザインを参照してダウンロードの拡張子を切り替える)。
    /// 形式の追加や外部システム連携などの拡張はここを起点に行う。
    /// </summary>
    public static class BulkFileTransfer
    {
        const int MaxRows = 500;

        /// <summary>一括ダウンロード。検索条件で取得した一覧をファイルバイナリにする。</summary>
        public static async Task<MemoryStream> GetListFileAsync(DesignData designData, ModuleDataIO moduleDataIO, SearchCondition condition)
        {
            var texts = await moduleDataIO.GetTableTextsAsync(condition);
            var csv = FindCsvFileTransfer(designData, condition.ModuleName);
            return csv != null
                ? CsvUtils.CreateCsvBinary(texts, csv.Encoding)
                : ExcelUtils.CreateExcelBinary(texts, "data");
        }

        /// <summary>一括更新。アップロードされたファイル (xlsx / CSV は内容で自動判定) を取り込む。</summary>
        public static async Task<List<ModuleSubmitResult>> SubmitByFileAsync(DesignData designData, ModuleDataIO moduleDataIO, string? moduleName, Stream file)
        {
            var csv = FindCsvFileTransfer(designData, moduleName ?? string.Empty);
            var texts = csv != null
                ? await CsvUtils.ReadAllTextsFromFileBinary(file, csv.Encoding)
                : await ExcelUtils.ReadAllTextsFromExcelBinary(file);
            if (MaxRows < texts.Count) throw LowCodeException.Create($"File has a maximum of {MaxRows} rows");
            return await moduleDataIO.SubmitWithTransactionByTableTextsAsync(moduleName, texts);
        }

        static CsvFileTransferFieldDesign? FindCsvFileTransfer(DesignData designData, string moduleName)
            => designData.Modules.Find(moduleName)?.Fields.OfType<CsvFileTransferFieldDesign>().FirstOrDefault();
    }
}
