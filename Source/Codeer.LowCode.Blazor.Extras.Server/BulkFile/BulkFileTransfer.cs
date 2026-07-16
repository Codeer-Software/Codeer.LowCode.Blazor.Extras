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
    /// モジュールデザインの 2 つの独立した設定フィールドの組み合わせで動作が決まる:
    ///   - <see cref="CsvFileTransferFieldDesign"/> … ファイル形式 (CSV 化・エンコーディング・区切り文字・拡張子)
    ///   - <see cref="MappedFileTransferFieldDesign"/> … 列構成 (相手仕様の列並び・書式・コード変換)
    /// なし = 従来の xlsx / Csv のみ = 内部名ヘッダの CSV / Mapped のみ = 外部列の xlsx / 両方 = 外部列の CSV (WebEDI)。
    /// クライアントも同じデザインを参照してダウンロードの拡張子を切り替える。
    /// 形式の追加や外部システム連携などの拡張はここを起点に行う。
    /// </summary>
    public static class BulkFileTransfer
    {
        /// <summary>一括ダウンロード。検索条件で取得した一覧をファイルバイナリにする。</summary>
        public static async Task<MemoryStream> GetListFileAsync(DesignData designData, ModuleDataIO moduleDataIO, SearchCondition condition)
        {
            var (csv, mapped) = FindTransferFields(designData, condition.ModuleName);

            var texts = await moduleDataIO.GetTableTextsAsync(condition);
            if (mapped != null) texts = await MappedFileTransform.ToExternalAsync(texts, mapped, moduleDataIO);

            return csv != null
                ? CsvUtils.CreateCsvBinary(texts, csv.Encoding, csv.GetDelimiterChar())
                : ExcelUtils.CreateExcelBinary(texts, "data");
        }

        /// <summary>
        /// 一括更新。アップロードされたファイルを検証して取り込む。
        /// 検証エラー (行番号付き) がある場合は取り込まずエラーレポートを返す。
        /// dryRun = true はファイル解析・マッピング・型の事前チェックだけ行い、取込は実行しない
        /// (事前チェックをすり抜けた不正データも、取込本体の例外とトランザクションロールバックで守られる)。
        /// </summary>
        public static async Task<List<ModuleSubmitResult>> SubmitByFileAsync(DesignData designData, ModuleDataIO moduleDataIO, string? moduleName, Stream file, bool dryRun = false)
        {
            var (csv, mapped) = FindTransferFields(designData, moduleName ?? string.Empty);

            //ファイル → テーブルテキスト (CSV フィールドがあれば内容で CSV/xlsx を自動判定)
            var texts = csv != null
                ? await CsvUtils.ReadAllTextsFromFileBinary(file, csv.Encoding, csv.GetDelimiterChar())
                : await ExcelUtils.ReadAllTextsFromExcelBinary(file);

            //外部列 → 内部テーブルテキスト (コード変換の引き当て失敗は行番号付きエラー)
            var mappedErrors = new List<string>();
            if (mapped != null) (texts, mappedErrors) = await MappedFileTransform.ToInternalAsync(texts, mapped, moduleDataIO);

            //取込前検証 (対応しない列・型変換できないセルを行番号付きで報告)
            var validationErrors = mappedErrors
                .Concat(TableTextsValidator.Validate(designData, moduleName, texts))
                .ToList();
            if (validationErrors.Any()) return Error(string.Join(Environment.NewLine, Cap(validationErrors)));

            if (dryRun) return [new ModuleSubmitResult()]; //検証のみ (エラーなし)

            return await moduleDataIO.SubmitWithTransactionByTableTextsAsync(moduleName, texts);
        }

        static (CsvFileTransferFieldDesign? Csv, MappedFileTransferFieldDesign? Mapped) FindTransferFields(DesignData designData, string moduleName)
        {
            var fields = designData.Modules.Find(moduleName)?.Fields;
            return (fields?.OfType<CsvFileTransferFieldDesign>().FirstOrDefault(),
                    fields?.OfType<MappedFileTransferFieldDesign>().FirstOrDefault());
        }

        static List<ModuleSubmitResult> Error(string message)
            => [new ModuleSubmitResult { ExceptionMessage = message }];

        static IEnumerable<string> Cap(List<string> errors)
        {
            const int max = 20;
            foreach (var e in errors.Take(max)) yield return e;
            if (max < errors.Count) yield return $"...and {errors.Count - max} more errors.";
        }
    }
}
