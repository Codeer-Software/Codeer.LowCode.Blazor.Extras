using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 編集履歴の契約解決。履歴モジュールは自分に置かれた契約フィールドで「役割→フィールド名」を宣言し、
    /// 対象モジュールは EditHistoryField で「履歴を取る」ことと履歴モジュールを宣言する。
    /// クライアント (EditHistoryField) とサーバー (EditHistoryRecorder) が同じ解決を使う。
    /// </summary>
    internal static class EditHistoryContracts
    {
        internal static EditHistoryContractFieldDesign? Contract(ModuleDesign? historyModule)
            => historyModule?.Fields.OfType<EditHistoryContractFieldDesign>().FirstOrDefault();

        internal static EditHistoryFieldDesign? Field(ModuleDesign? targetModule)
            => targetModule?.Fields.OfType<EditHistoryFieldDesign>().FirstOrDefault();

        /// <summary>
        /// 対象モジュールから履歴モジュールと契約を解決する。EditHistoryField が無ければ null (= 履歴を取らない)。
        /// 履歴モジュール・契約が欠けていれば error に理由 (デザインチェックが指摘する不備)。
        /// </summary>
        internal static (ModuleDesign HistoryModule, EditHistoryContractFieldDesign Names)? Resolve(
            DesignData designData, ModuleDesign targetModule, out string? error)
        {
            error = null;
            var field = Field(targetModule);
            if (field == null) return null;
            var historyModule = designData.Modules.Find(field.HistoryModuleName);
            if (historyModule == null)
            {
                error = $"Edit history module '{field.HistoryModuleName}' of '{targetModule.Name}' does not exist.";
                return null;
            }
            var names = Contract(historyModule);
            if (names == null)
            {
                error = $"Edit history module '{historyModule.Name}' has no {nameof(EditHistoryContractFieldDesign)}.";
                return null;
            }
            return (historyModule, names);
        }

        /// <summary>
        /// あるレコードの版を新しい順に引く条件。並びは日時 (DateTime 役割があれば) の降順、同着は Id の降順。
        /// 版一覧・復活ボタンの「最新の版か」・サーバーの復活可否は、必ずこの同じ条件を使う (並びが食い違うと判定が割れる)。
        /// </summary>
        internal static SearchCondition VersionsCondition(string historyModuleName, EditHistoryContractFieldDesign names,
            string moduleName, string dataId, int? limitCount, params string[] selectFields)
        {
            var condition = new SearchCondition
            {
                ModuleName = historyModuleName,
                Condition = MultiMatchCondition.And(Equal(names.ModuleName, moduleName), Equal(names.DataId, dataId)),
                LimitCount = limitCount,
                SortConditions = new List<SortCondition>(),
                SelectFields = selectFields.Where(e => !string.IsNullOrEmpty(e)).ToList(),
            };
            if (!string.IsNullOrEmpty(names.DateTime))
                condition.SortConditions.Add(new SortCondition { Variable = $"{names.DateTime}.Value", IsDescending = true });
            condition.SortConditions.Add(new SortCondition { Variable = $"{SystemFieldNames.Id}.Value", IsDescending = true });
            return condition;
        }

        static FieldValueMatchCondition Equal(string fieldName, string value) => new()
        {
            SearchTargetVariable = $"{fieldName}.Value", Comparison = MatchComparison.Equal, Value = MultiTypeValue.Create(value),
        };

        /// <summary>履歴行の文字列の項目 (役割のフィールド名が空・項目が無ければ空)。</summary>
        internal static string GetText(ModuleData row, string fieldName)
            => string.IsNullOrEmpty(fieldName) ? string.Empty : (row.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value ?? string.Empty;

        /// <summary>モジュールの従属レコードの宣言 (IOwnedRecordsFieldDesign) を、宣言したフィールドと組で列挙する。</summary>
        internal static IEnumerable<(FieldDesignBase Field, OwnedRecordsDesign Owned)> OwnedRecords(IModuleDesigns modules, ModuleDesign design)
            => design.Fields.OfType<IOwnedRecordsFieldDesign>().SelectMany(e => e.GetOwnedRecords(modules).Select(o => ((FieldDesignBase)e, o)));

        /// <summary>
        /// スナップショット・差分・復元の対象外にするフィールドか。
        /// システムフィールド (Id / 楽観ロック / 作成・更新・削除の記録 / 論理削除) とリンク越し (ドット名) の派生値は対象外。
        /// </summary>
        internal static bool IsExcludedField(string fieldName) => OperatingModel.OwnedRecordsExtensions.IsSystemOrLinkedField(fieldName);

        /// <summary>行の内容を持つ従属レコードのデータか (一覧・内容を持つ埋め込みモジュール。参照だけのものは含まない)。</summary>
        internal static bool HasOwnedRows(FieldDataBase? data)
            => data is IOwnedRecordsData owned && owned.GetOwnedRows() != null;
    }
}
