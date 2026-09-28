using Codeer.LowCode.Blazor.Json;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 削除の取り消し (復活) の依頼。クライアントは「どの履歴行 (版) か」だけを送り、戻す行の Id はサーバーがその版のスナップショットから組み立てる
    /// (クライアントが Id を指定できると、権限の外の行を戻せてしまうため)。
    /// EditHistoryField (この版に戻す) と EditHistoryUndeleteButtonField (復活ボタン) が Submit の ExtendedData に載せ、
    /// サーバーの EditHistoryRecorder が本体の保存の前に処理する (同じトランザクション = 戻した行の Update と一緒に確定する)。
    /// </summary>
    public class EditHistoryUndeleteData : JsonAbstract
    {
        public EditHistoryUndeleteData() : base(typeof(EditHistoryUndeleteData).FullName!) { }

        /// <summary>履歴モジュール名。</summary>
        public string HistoryModuleName { get; set; } = string.Empty;

        /// <summary>履歴行 (版) の Id。</summary>
        public string HistoryRowId { get; set; } = string.Empty;

        /// <summary>
        /// true = 復活ボタン: 削除の版からレコード全体を戻す (論理削除は Id を保って取り消し、物理削除はスナップショットから作り直す)。
        /// false = この版に戻す: 版の従属レコードのうち論理削除になっている行だけを Id を保って取り消す (レコード自身と行の値はフォームの保存が持つ)。
        /// </summary>
        public bool RestoreWholeRecord { get; set; }
    }
}
