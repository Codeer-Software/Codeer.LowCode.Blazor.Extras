using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.SemanticSearch
{
    /// <summary>
    /// 意味検索の条件: SemanticSearchField (<see cref="FieldSqlMatchCondition.FieldName"/>) の索引が <see cref="Vector"/> に近い行。
    /// 一覧検索 (ModuleDataIO.GetListAsync) の条件に置くと、本体の権限 (モジュールの閲覧権限・行の条件・論理削除・項目の読み取り権限) が効いたまま
    /// DB のベクトル検索 (pgvector / SQL Server 2025) で絞り・並べられる。近い順に並べるときは SortConditions に "フィールド名.Value" (昇順) を足す。
    /// ベクトルは呼び出す側が埋め込みプロバイダで作って入れるか、<see cref="Text"/> だけを入れてサーバーの前処理 (Extras.Server の SemanticSearchService の
    /// ConditionInterceptor。ホストが ModuleDataIO に登録する) に埋めさせる (画面の検索欄はこちら)。ベクトル検索に対応しない DB のモジュールでは例外になる。
    /// </summary>
    public class SemanticMatchCondition : FieldSqlMatchCondition
    {
        public SemanticMatchCondition() : base(typeof(SemanticMatchCondition).FullName!) { }

        /// <summary>探したい内容 (文章)。<see cref="Vector"/> が空なら、サーバーの前処理がこの文章を埋め込みにする。</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>探したい内容の埋め込みベクトル (索引と同じ埋め込みモデルで作ったもの)。</summary>
        public float[] Vector { get; set; } = [];

        /// <summary>コサイン距離の上限 (0 = 同じ向き〜2)。この距離より遠い行は出さない。null なら距離では絞らない (並べるだけ)。</summary>
        public double? MaxDistance { get; set; }
    }
}
