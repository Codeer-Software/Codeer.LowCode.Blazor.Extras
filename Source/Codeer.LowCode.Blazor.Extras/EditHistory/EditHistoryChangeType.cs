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
}
