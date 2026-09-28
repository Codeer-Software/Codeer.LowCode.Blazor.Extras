using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 編集履歴の変更種別 (履歴モジュールの ChangeType 値)。
    /// [DesignEnum] によりデザイン enum として公開される (enum 定義ファイル不要。
    /// SelectField の EnumName・条件エディタの値候補・スクリプトから使える)。
    /// メンバー名 = DB 保存値のため変更不可。
    /// </summary>
    [DesignEnum]
    public enum EditHistoryChangeType
    {
        [DesignEnumMember(DisplayText = "$EditHistoryChangeType_Add")]
        Add,
        [DesignEnumMember(DisplayText = "$EditHistoryChangeType_Update")]
        Update,
        [DesignEnumMember(DisplayText = "$EditHistoryChangeType_Delete")]
        Delete,
        /// <summary>削除されたレコードの復活 (論理削除の取り消し)。</summary>
        [DesignEnumMember(DisplayText = "$EditHistoryChangeType_Restore")]
        Restore,
    }

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

        /// <summary>モジュールの従属レコードの宣言 (IOwnedRecordsFieldDesign) を、宣言したフィールドと組で列挙する。</summary>
        internal static IEnumerable<(FieldDesignBase Field, OwnedRecordsDesign Owned)> OwnedRecords(ModuleDesign design)
            => design.Fields.OfType<IOwnedRecordsFieldDesign>().SelectMany(e => e.GetOwnedRecords().Select(o => ((FieldDesignBase)e, o)));

        /// <summary>
        /// 論理削除のモジュールか (LogicalDelete / DeletedAt / Deleter のどれかがあり、退避 (DeleteArchive) でない)。
        /// 論理削除なら削除した行を Id を保ったまま戻せる (ModuleDataIO.UndeleteAsync)。
        /// </summary>
        internal static bool IsLogicalDeleteModule(ModuleDesign design)
            => !design.Fields.Any(e => e is DeleteArchiveFieldDesign) &&
               design.Fields.Any(e => e.Name is SystemFieldNames.LogicalDelete or SystemFieldNames.DeletedAt or SystemFieldNames.Deleter);

        /// <summary>
        /// スナップショット・差分・復元の対象外にするフィールドか。
        /// システムフィールド (Id / 楽観ロック / 作成・更新・削除の記録 / 論理削除) とリンク越し (ドット名) の派生値は対象外。
        /// </summary>
        internal static bool IsExcludedField(string fieldName)
            => _systemFields.Contains(fieldName) || new FieldName(fieldName).IsLink;

        static readonly HashSet<string> _systemFields =
        [
            SystemFieldNames.Id, SystemFieldNames.OptimisticLocking, SystemFieldNames.LogicalDelete,
            SystemFieldNames.CreatedAt, SystemFieldNames.UpdatedAt, SystemFieldNames.DeletedAt,
            SystemFieldNames.Creator, SystemFieldNames.Updater, SystemFieldNames.Deleter,
            SystemFieldNames.CurrentUser, SystemFieldNames.DeleteArchive,
        ];
    }
}
