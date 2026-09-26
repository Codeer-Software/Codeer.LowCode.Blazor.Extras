using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>履歴の 1 版 (履歴モジュールの 1 行)。</summary>
    public class EditHistoryVersion
    {
        /// <summary>履歴行の Id。</summary>
        public string Id { get; internal set; } = string.Empty;

        /// <summary>版番号 (そのレコードの履歴の古い方から 1, 2, ...)。閲覧時に件数から採番する。</summary>
        public int Number { get; internal set; }

        /// <summary>変更種別 (EditHistoryChangeType のメンバー名)。</summary>
        public string ChangeType { get; internal set; } = string.Empty;

        /// <summary>変更したユーザーの表示文字列 (UserId 役割が空なら空)。</summary>
        public string UserText { get; internal set; } = string.Empty;

        /// <summary>変更日時 (DateTime 役割が空なら null)。</summary>
        public DateTime? DateTime { get; internal set; }

        /// <summary>この版のレコード全体 (削除は削除前の内容)。</summary>
        [ScriptHide]
        public ModuleData? Snapshot { get; internal set; }

        /// <summary>差分の元 (前の版) があるか。履歴を取り始める前からあったレコードの最初の更新は前の版が無いので差分を出せない。</summary>
        public bool HasPreviousVersion { get; internal set; } = true;

        /// <summary>前の版との差分 (閲覧権限のあるフィールドだけ)。</summary>
        [ScriptHide]
        public List<EditHistoryChange> Changes { get; internal set; } = new();

        public bool IsDelete => ChangeType == EditHistoryChangeType.Delete.ToString();
        public bool IsAdd => ChangeType == EditHistoryChangeType.Add.ToString();
        public bool IsRestore => ChangeType == EditHistoryChangeType.Restore.ToString();
    }

    /// <summary>1 フィールドの変更。一覧 (明細) は行の追加・削除・変更として持つ。</summary>
    public class EditHistoryChange
    {
        public string FieldName { get; internal set; } = string.Empty;
        public string DisplayName { get; internal set; } = string.Empty;
        public string Before { get; internal set; } = string.Empty;
        public string After { get; internal set; } = string.Empty;

        /// <summary>Before / After の文字列があるか。文字列化できない型 (独自のデータクラス) は名前だけ出す。</summary>
        public bool HasValueText { get; internal set; } = true;
        public bool IsList { get; internal set; }
        public List<EditHistoryRowChange> Rows { get; internal set; } = new();
        public int AddedCount => Rows.Count(e => e.Kind == EditHistoryRowChangeKind.Added);
        public int RemovedCount => Rows.Count(e => e.Kind == EditHistoryRowChangeKind.Removed);
        public int ChangedCount => Rows.Count(e => e.Kind == EditHistoryRowChangeKind.Changed);
    }

    public enum EditHistoryRowChangeKind { Added, Removed, Changed }

    /// <summary>明細 1 行の変更。</summary>
    public class EditHistoryRowChange
    {
        public EditHistoryRowChangeKind Kind { get; internal set; }

        /// <summary>行番号 (その版の明細の並びでの位置。削除された行は前の版での位置)。1 始まり。</summary>
        public int RowNumber { get; internal set; }

        /// <summary>変更行はフィールド差分、追加・削除行はその行の値 (追加は After、削除は Before に入る)。</summary>
        public List<EditHistoryChange> Changes { get; internal set; } = new();

        /// <summary>その行のデータ (追加・変更はこの版の行、削除は前の版の行)。「この版を表示」の強調と削除行の表示に使う。</summary>
        [ScriptHide]
        public ModuleData? Row { get; internal set; }
    }
}
