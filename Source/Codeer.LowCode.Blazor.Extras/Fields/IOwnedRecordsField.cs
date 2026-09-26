using Codeer.LowCode.Blazor.Repository.Data;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 従属レコードを宣言したフィールド (IOwnedRecordsFieldDesign) のランタイム側。
    /// 編集履歴の復元などが、宣言した従属レコード群を版の内容に差し替えるために使う (保存はユーザーが行う)。
    /// </summary>
    public interface IOwnedRecordsField
    {
        /// <param name="name">宣言の Name (OwnedRecordsDesign.Name)。</param>
        /// <param name="rows">差し替え後の行 (スナップショットの行データ)。</param>
        /// <param name="onRevive">論理削除の行を Id を保って戻すときの登録先 (モジュール名, Id)。null なら常に新しい行として追加する。</param>
        Task ApplyOwnedRecordsAsync(string name, List<ModuleData> rows, Action<string, string>? onRevive);

        /// <summary>版表示など、DB を読まずに与えられた行をそのまま表示専用で見せる。</summary>
        /// <param name="name">宣言の Name (OwnedRecordsDesign.Name)。</param>
        /// <param name="rows">表示する行 (スナップショットの行データ)。</param>
        Task ShowOwnedRecordsAsync(string name, List<ModuleData> rows);
    }
}
