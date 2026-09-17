using Codeer.LowCode.Blazor;
using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Extras.BulkFile;
using Codeer.LowCode.Blazor.Extras.Designs;
using System.Reflection;
using Codeer.LowCode.Blazor.Extras.Server.Csv;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Excel.Report.PDF;

namespace Codeer.LowCode.Blazor.Extras.Server.BulkFile
{
    /// <summary>
    /// 一覧の一括ダウンロード/一括更新 (list_file / submit_by_file) のサーバー処理本体。
    /// テンプレートの ModuleDataController から移譲される。
    /// モジュールデザインの 3 つの独立した設定フィールドの組み合わせで動作が決まる:
    ///   - <see cref="CsvFileFormatFieldDesign"/> … ファイル形式 (CSV 化・エンコーディング・区切り文字・拡張子)
    ///   - <see cref="FileColumnMappingFieldDesign"/> … 列構成 (相手仕様の列並び・固定値・固定長の幅)
    ///   - <see cref="FileValueConversionFieldDesign"/> … 値の表し方 (対象フィールドの外部値⇔内部値の引き当て。列構成と独立にどの経路でも効く)
    /// なし = 従来の xlsx / Csv のみ = 内部名ヘッダの CSV / 列マッピングのみ = 外部列の xlsx / 両方 = 外部列の CSV (WebEDI)。
    /// さらに Csv フィールドの Delimiter が None (区切り文字なし) なら両方の組み合わせが固定長形式になる
    /// (形式 = 幅の単位・エンコーディング・拡張子は CsvFileFormatFieldDesign、
    /// 列幅は列構成と不可分なため FileColumnMappingFieldDesign の各列。併用必須 = デザインチェック)。
    /// 列マッピングは ModuleData ⇔ 外部列の型付き変換 (テーブルテキストを経由しない)、
    /// 列マッピングなしは内部名ヘッダのテーブルテキストのラウンドトリップ (値の引き当ては見出しを変えずセルの値だけ置き換える)。
    /// クライアントも同じデザインを参照してダウンロードの拡張子を切り替える。
    /// 形式の追加や外部システム連携などの拡張はここを起点に行う。
    /// </summary>
    public static class BulkFileTransfer
    {
        /// <summary>一括ダウンロード。検索条件で取得した一覧をファイルバイナリにする。</summary>
        public static async Task<MemoryStream> GetListFileAsync(DesignData designData, ModuleDataIO moduleDataIO, SearchCondition condition)
        {
            var (module, csv, mapping) = FindTransferFields(designData, condition.ModuleName);

            var texts = mapping != null
                ? await FileColumnMappingTransform.ToExternalAsync((await moduleDataIO.GetListAsync(condition, 0)).Items, mapping, module!, moduleDataIO)
                : await ToExternalValuesAsync(await moduleDataIO.GetTableTextsAsync(condition), module, moduleDataIO);

            //固定長形式 (幅に収まらない値は行番号付きエラーで失敗する。黙って切り詰めない)
            if (IsFixedLength(csv, mapping))
                return FixedLengthUtils.CreateFixedLengthBinary(texts, mapping!, csv!);

            return csv != null
                ? CsvUtils.CreateCsvBinary(texts, csv.Encoding, csv.Delimiter.ToChar())
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
            var (module, csv, mapping) = FindTransferFields(designData, moduleName ?? string.Empty);

            //ファイル → テーブルテキスト (固定長/CSV は内容で xlsx との自動判定あり)
            var texts = IsFixedLength(csv, mapping)
                ? await FixedLengthUtils.ReadAllTextsFromFileBinary(file, mapping!, csv!)
                : csv != null
                ? await CsvUtils.ReadAllTextsFromFileBinary(file, csv.Encoding, csv.Delimiter.ToChar())
                : await ExcelUtils.ReadAllTextsFromExcelBinary(file);

            //相手仕様の列 → 型付きで ModuleData に変換して取込 (解釈できない値・引き当て失敗は行番号付きエラー)
            if (mapping != null)
            {
                var (items, mappingErrors) = await FileColumnMappingTransform.ToInternalAsync(texts, mapping, module!, moduleDataIO);
                if (mappingErrors.Any()) return Error(string.Join(Environment.NewLine, Cap(mappingErrors)));
                if (dryRun) return [new ModuleSubmitResult()]; //検証のみ (エラーなし)
                return await moduleDataIO.SubmitWithTransactionByModuleDataAsync(moduleName, items);
            }

            //内部名ヘッダのテーブルテキスト取込。値の引き当て (外部値→内部値。引き当て失敗は行番号付きエラー) →
            //取込前検証 (対応しない列・型変換できないセルを行番号付きで報告)
            var hasConversion = module?.Fields.OfType<FileValueConversionFieldDesign>().Any() == true;
            var errors = new List<string>();
            if (hasConversion)
            {
                var (converted, conversionErrors) = await FileValueConversionTransform.ToInternalAsync(texts, module!, moduleDataIO);
                texts = converted;
                errors.AddRange(conversionErrors);
            }
            errors.AddRange(TableTextsValidator.Validate(designData, moduleName, texts));
            if (errors.Any()) return Error(string.Join(Environment.NewLine, Cap(errors)));

            if (dryRun) return [new ModuleSubmitResult()]; //検証のみ (エラーなし)

            if (!hasConversion) return await moduleDataIO.SubmitWithTransactionByTableTextsAsync(moduleName, texts);

            //値の引き当てがあるモジュールは列マッピングと同じ型付き経路で取り込む (空セル (空白だけも含む) は null)。
            //本体のテキスト経路は空セルを string メンバに空文字で入れるため、参照 (Link) 列の空セルが DB の数値列で失敗し、
            //Id 付き更新で参照を外せない。型付き経路は本体の保護フィールド差分チェック (テキスト経路だけの機能) を通らない点は列マッピング経路と同じ
            var parsed = InternalNameTableTextsToModuleData(texts, module!);
            if (parsed.Errors.Any())
                return Error(string.Join(Environment.NewLine, Cap(parsed.Errors.Select(e => $"Row {e.FileRow}, {e.ColumnLabel}: {e.Message}").ToList())));
            return await moduleDataIO.SubmitWithTransactionByModuleDataAsync(moduleName, parsed.Items);
        }

        //固定長は形式 (Delimiter = None) と列幅 (列マッピング) の両方が揃って成立する
        //(片方だけはデザインチェックエラー。実行時は従来動作へ寛容にフォールバック)
        /// <summary>
        /// スクリプトの一括ファイル出力 (list_file_by_data / BulkFileTransferService.Download(List&lt;Module&gt;)) 用。
        /// クライアントで加工済みのモジュールデータ列をそのままファイル化する (検索しない点が GetListFileAsync と違う)。
        /// 形式は一括ダウンロードと同じ分岐 (固定長/CSV/xlsx、列マッピングがあれば相手仕様の列、なければ内部名ヘッダ)。
        /// </summary>
        public static async Task<MemoryStream> GetListFileByDataAsync(DesignData designData, ModuleDataIO moduleDataIO, string? moduleName, List<ModuleData> items)
        {
            var (module, csv, mapping) = FindTransferFields(designData, moduleName ?? string.Empty);
            if (module == null) return new MemoryStream();

            var texts = mapping != null
                ? await FileColumnMappingTransform.ToExternalAsync(items, mapping, module, moduleDataIO)
                : await ToExternalValuesAsync(ModuleDataToInternalNameTableTexts(items, module), module, moduleDataIO);

            if (IsFixedLength(csv, mapping))
                return FixedLengthUtils.CreateFixedLengthBinary(texts, mapping!, csv!);

            return csv != null
                ? CsvUtils.CreateCsvBinary(texts, csv.Encoding, csv.Delimiter.ToChar())
                : ExcelUtils.CreateExcelBinary(texts, "data");
        }

        /// <summary>
        /// list_file_by_data のリクエストボディ (JsonConverterEx 直列化の List&lt;ModuleData&gt;) を復元して
        /// <see cref="GetListFileByDataAsync(DesignData, ModuleDataIO, string?, List{ModuleData})"/> に移譲する。
        /// ワイヤ形式は送信側 (BulkFileTransferService) とここで対になるため、テンプレートの Controller は移譲だけにする。
        /// </summary>
        public static async Task<MemoryStream> GetListFileByDataAsync(DesignData designData, ModuleDataIO moduleDataIO, string? moduleName, Stream body)
            => await GetListFileByDataAsync(designData, moduleDataIO, moduleName, await ReadModuleDataListAsync(body));

        /// <summary>
        /// スクリプトの一括保存 (bulk_submit / BulkFileTransferService.Submit) 用。
        /// リクエストボディ (JsonConverterEx 直列化の List&lt;ModuleData&gt;) を復元して一括保存し
        /// (ファイル取込と同じ Id の一致で追加/更新判定・1トランザクション)、
        /// クライアントが復元する応答 JSON (List&lt;ModuleSubmitResult&gt;) を返す。
        /// ワイヤ形式は送信側 (BulkFileTransferService) とここで対になるため、テンプレートの Controller は移譲だけにする。
        /// </summary>
        public static async Task<string> BulkSubmitAsync(ModuleDataIO moduleDataIO, string? moduleName, Stream body)
            => JsonConverterEx.SerializeObject(
                await moduleDataIO.SubmitWithTransactionByModuleDataAsync(moduleName, await ReadModuleDataListAsync(body)));

        //リクエストボディ (JsonConverterEx 直列化の List<ModuleData>) の復元
        static async Task<List<ModuleData>> ReadModuleDataListAsync(Stream body)
        {
            using var reader = new StreamReader(body);
            return JsonConverterEx.DeserializeObject<List<ModuleData>>(await reader.ReadToEndAsync()) ?? new();
        }

        //内部名ヘッダのテーブルテキストに値の引き当て (内部値→外部値) を適用する (FileValueConversionField が無ければそのまま)
        static Task<List<List<string>>> ToExternalValuesAsync(List<List<string>> texts, ModuleDesign? module, ModuleDataIO moduleDataIO)
            => module == null ? Task.FromResult(texts) : FileValueConversionTransform.ToExternalAsync(texts, module, moduleDataIO);

        //ModuleData → 内部名ヘッダ ("フィールド名.データメンバ名") のテーブルテキスト (取込側の逆方向)。
        //列 = デザインの DbColumn プロパティ (DataMember) 規約。値は ToString (書式なし)
        static List<List<string>> ModuleDataToInternalNameTableTexts(List<ModuleData> items, ModuleDesign module)
        {
            var targets = new List<(string Header, string FieldName, System.Reflection.PropertyInfo Property)>();
            foreach (var field in module.Fields)
            {
                if (field.GetType().GetCustomAttribute<DisableBulkDataUpdateAttribute>(true) != null) continue;
                var dataType = field.CreateData()?.GetType();
                if (dataType == null) continue;
                foreach (var prop in field.GetType().GetProperties())
                {
                    var attr = prop.GetCustomAttribute<DbColumnAttribute>();
                    if (attr == null) continue;
                    if (string.IsNullOrEmpty(prop.GetValue(field) as string)) continue; //DB列未割当
                    var dataProp = dataType.GetProperty(attr.DataMember);
                    if (dataProp == null) continue;
                    targets.Add(($"{field.Name}.{attr.DataMember}", field.Name, dataProp));
                }
            }

            var texts = new List<List<string>> { targets.Select(t => t.Header).ToList() };
            foreach (var item in items)
            {
                var row = new List<string>();
                foreach (var t in targets)
                {
                    var value = item.Fields.TryGetValue(t.FieldName, out var data) ? t.Property.GetValue(data) : null;
                    row.Add(value?.ToString() ?? string.Empty);
                }
                texts.Add(row);
            }
            return texts;
        }

        /// <summary>
        /// スクリプトの一括ファイル取込 (parse_file / BulkFileReader) 用の解析。DB には書き込まない。
        /// 形式は一括更新と同じ分岐 (固定長/CSV/xlsx 自動判定、列マッピングがあれば相手仕様の列、なければ内部名ヘッダ)。
        /// 解釈できないセルは値未設定のまま Errors に載り、行は捨てない
        /// (SubmitByFileAsync の「エラーがあれば取り込まない」と思想が違う点。トレーラ行などはスクリプト側で捨てる)。
        /// </summary>
        public static async Task<BulkFileParseResult> ParseFileAsync(DesignData designData, ModuleDataIO moduleDataIO, string? moduleName, Stream file)
        {
            var (module, csv, mapping) = FindTransferFields(designData, moduleName ?? string.Empty);
            if (module == null) return new BulkFileParseResult();

            var texts = IsFixedLength(csv, mapping)
                ? await FixedLengthUtils.ReadAllTextsFromFileBinary(file, mapping!, csv!)
                : csv != null
                ? await CsvUtils.ReadAllTextsFromFileBinary(file, csv.Encoding, csv.Delimiter.ToChar())
                : await ExcelUtils.ReadAllTextsFromExcelBinary(file);

            if (mapping != null)
            {
                var (items, errors) = await FileColumnMappingTransform.ToInternalWithCellErrorsAsync(texts, mapping, module, moduleDataIO);
                return new BulkFileParseResult { Items = items, Errors = errors };
            }

            //値の引き当て (引き当てられないセルは値未設定 + エラー) → 内部名ヘッダの解析。エラーは行順に並べる
            var (converted, conversionErrors) = await FileValueConversionTransform.ToInternalWithCellErrorsAsync(texts, module, moduleDataIO);
            var result = InternalNameTableTextsToModuleData(converted, module);
            result.Errors = conversionErrors.Concat(result.Errors).OrderBy(e => e.FileRow).ToList();
            return result;
        }

        //内部名ヘッダ ("フィールド名.データメンバ名") のテーブルテキスト → ModuleData。
        //取込本体と同じ規約 (FieldData のデータメンバ解決 + DisableBulkDataUpdate 除外) で列を解決し、
        //解決できない列は無視、型変換できないセルはエラー (値未設定)
        static BulkFileParseResult InternalNameTableTextsToModuleData(List<List<string>> texts, ModuleDesign module)
        {
            var result = new BulkFileParseResult();
            if (texts.Count == 0) return result;

            var header = texts[0];
            var targets = new List<(int Index, string Header, FieldDesignBase Field, System.Reflection.PropertyInfo Property)>();
            for (var i = 0; i < header.Count; i++)
            {
                var sp = header[i].Split('.', 2);
                var field = module.Fields.FirstOrDefault(f => f.Name == sp[0]);
                if (field == null) continue;
                if (field.GetType().GetCustomAttribute<DisableBulkDataUpdateAttribute>(true) != null) continue;
                var property = field.CreateData()?.GetType().GetProperty(sp.Length == 2 ? sp[1] : "Value");
                if (property == null) continue;
                targets.Add((i, header[i], field, property));
            }

            var fileRow = 1;
            foreach (var row in texts.Skip(1))
            {
                fileRow++;
                var data = new ModuleData { Name = module.Name };
                foreach (var t in targets)
                {
                    var text = t.Index < row.Count ? row[t.Index] : string.Empty;
                    //空セル (空白だけも含む) は列の種類によらず null (未設定)。列マッピング経路と同じ
                    //(参照 (Link) 列は string メンバでも DB 列は数値のことがあり、空文字のまま書き込むと取込本体で失敗する)
                    object? value = null;
                    if (!string.IsNullOrWhiteSpace(text) && !BulkDataTextConverter.TryConvert(text, t.Property.PropertyType, out value))
                    {
                        result.Errors.Add(new BulkFileCellError
                        {
                            ItemIndex = result.Items.Count,
                            FileRow = fileRow,
                            FieldName = t.Field.Name,
                            ColumnLabel = t.Header,
                            Message = $"cannot convert '{text}'."
                        });
                        continue;
                    }
                    if (!data.Fields.TryGetValue(t.Field.Name, out var fieldData))
                    {
                        var created = t.Field.CreateData();
                        if (created == null) continue;
                        fieldData = created;
                        data.Fields[t.Field.Name] = fieldData;
                    }
                    t.Property.SetValue(fieldData, value);
                }
                result.Items.Add(data);
            }
            return result;
        }

        static bool IsFixedLength(CsvFileFormatFieldDesign? csv, FileColumnMappingFieldDesign? mapping)
            => csv != null && csv.Delimiter == CsvDelimiterKind.None && mapping != null;

        static (ModuleDesign? Module, CsvFileFormatFieldDesign? Csv, FileColumnMappingFieldDesign? Mapping) FindTransferFields(
            DesignData designData, string moduleName)
        {
            var module = designData.Modules.Find(moduleName);
            return (module,
                    module?.Fields.OfType<CsvFileFormatFieldDesign>().FirstOrDefault(),
                    module?.Fields.OfType<FileColumnMappingFieldDesign>().FirstOrDefault());
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
