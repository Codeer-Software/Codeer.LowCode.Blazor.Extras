using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 行を「意味で探せる」ようにする書き込み専用フィールド (UI なし)。
    /// Submit のたびに <see cref="SourceFields"/> の値を「表示名: 値」の行に並べた文章を作り (クライアント側)、
    /// サーバーの SemanticSearchIndexer がその文章の埋め込みベクトルを付けて、2 つの書き込み専用列に保存する。
    /// AI チャット (RawDataAccessAgent) はこの列を使って「似た記録」を探す (search_records)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TextSearch")]
    [Designer(DisplayName = "$SemanticSearchField")]
    [IgnoreBaseProperties(nameof(IgnoreModification), nameof(OnValidateInput), nameof(IsFocusSkip), nameof(OnFocusMoving), nameof(NextFocusField))]
    public class SemanticSearchFieldDesign() : FieldDesignBase(typeof(SemanticSearchFieldDesign).FullName!), IDataDependentField
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeColumnsRequired = 1;
        private const int CodeFieldsNotLoaded = 2;

        /// <summary>文章にするフィールド (同じモジュール)。空なら DB 列を持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成/更新の記録は除く)。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "$SemanticSearchFieldSourceFields")]
        public List<string> SourceFields { get; set; } = [];

        /// <summary>行を文章にしたものを保存する列 (書き込み専用)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnText"), DbColumn(nameof(SemanticSearchFieldData.Text), IsWriteOnly = true)]
        public string DbColumnText { get; set; } = string.Empty;

        /// <summary>埋め込みベクトルを保存する列 (書き込み専用。float32 の並びの base64 文字列)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnVector"), DbColumn(nameof(SemanticSearchFieldData.Vector), IsWriteOnly = true)]
        public string DbColumnVector { get; set; } = string.Empty;

        /// <summary>
        /// DB 側のベクトル検索 (pgvector / SQL Server 2025 の VECTOR 型) で距離計算に使うベクトル型の列。空なら DB 側検索を使わずサーバーのメモリで比較する。
        /// アプリは <see cref="DbColumnVector"/> にテキスト (JSON 配列) で書くので、PostgreSQL ではそれをキャストする生成列の名前、
        /// SQL Server のようにテキストから VECTOR 型列へ直接書ける DB では <see cref="DbColumnVector"/> と同じ列名を設定する。
        /// 接続先がベクトル検索に対応しない DB (SQLite 等) のときは設定があっても自動でメモリ比較に落ちる (開発環境で同じデザインを使える)。
        /// </summary>
        [Designer(Index = 5, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnVectorSearch")]
        public string DbColumnVectorSearch { get; set; } = string.Empty;

        /// <summary>文章の最大文字数 (超えた分は切り捨て。埋め込みモデルの入力上限の歯止め)。</summary>
        [Designer(Index = 6, DisplayName = "$SemanticSearchFieldMaxTextLength")]
        public int MaxTextLength { get; set; } = 8000;

        /// <summary>両方の列が設定されているか (検索と索引の対象になる条件)。</summary>
        public bool HasColumns => !string.IsNullOrWhiteSpace(DbColumnText) && !string.IsNullOrWhiteSpace(DbColumnVector);

        public override string GetWebComponentTypeFullName() => typeof(SemanticSearchFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new SemanticSearchField(this);
        public override FieldDataBase? CreateData() => new SemanticSearchFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (!HasColumns)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), CodeColumnsRequired),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = string.IsNullOrWhiteSpace(DbColumnText) ? nameof(DbColumnText) : nameof(DbColumnVector) },
                    Message = Properties.Resources.SemanticSearchCheck_ColumnsRequired,
                });
            }
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnText), DbColumnText).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnVector), DbColumnVector).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnVectorSearch), DbColumnVectorSearch).AddTo(result);
            foreach (var field in SourceFields)
                context.CheckFieldFieldExistence(Name, nameof(SourceFields), field).AddTo(result);
            result.AddRange(CheckSourceFieldsLoaded(context));
            return result;
        }

        /// <summary>
        /// 文章にするフィールドの値は、レイアウトに置いていなくてもフロントに来ていなければならない (文章はクライアントが組み立てるので、
        /// 読み込まれていないフィールドは文章から欠け、その行を保存すると欠けた文章で索引が上書きされる)。
        /// 詳細画面の読み込みは「レイアウトのフィールド + DataOnlyFields (+ それらの <see cref="IDataDependentField"/> の依存先)」だけなので、
        /// 各詳細レイアウトについて対象フィールドが読み込まれるかを見る。直し方は、欠けるフィールドか、このフィールド (SourceFields 設定時) をレイアウトか DataOnlyFields に含める。
        /// </summary>
        IEnumerable<DesignCheckInfo> CheckSourceFieldsLoaded(DesignCheckContext context)
        {
            var module = context.GetModuleDesign();
            if (module == null || !HasColumns) yield break;
            var sources = SemanticSearchText.SourceFields(module, this).Where(n => module.Fields.Any(f => f.Name == n)).ToList();
            foreach (var (layoutName, layout) in module.DetailLayouts)
            {
                List<string> placed;
                try { placed = layout.Layout.GetDescendantFields(module).Select(f => f.Name).ToList(); }
                catch (InvalidOperationException) { continue; } //存在しないフィールドがレイアウトにある = 別のチェックが指摘する
                var loaded = new HashSet<string>(placed.Concat(layout.DataOnlyFields));
                foreach (var dependent in loaded.Select(n => module.Fields.FirstOrDefault(f => f.Name == n)).OfType<IDataDependentField>().ToList())
                    loaded.UnionWith(dependent.GetDependencyFields());
                //対象フィールドを 1 つも読み込まないレイアウト (既定の空レイアウト等) は、そこから対象を編集できず更新で文章も送らないので対象外
                if (!sources.Any(loaded.Contains)) continue;
                var missing = sources.Where(n => !loaded.Contains(n)).ToList();
                if (missing.Count == 0) continue;
                yield return new LayoutDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), CodeFieldsNotLoaded),
                    Location = new() { Module = context.OwnerModule, LayoutType = ModuleLayoutType.Detail, Layout = layoutName, Member = nameof(DetailLayoutDesign.DataOnlyFields) },
                    Message = string.Format(Properties.Resources.SemanticSearchCheck_FieldsNotLoadedFormat, layoutName, string.Join(", ", missing)),
                };
            }
        }

        /// <summary>
        /// このフィールドがレイアウトか DataOnlyFields にあれば、文章にするフィールドも一緒に読み込む (本体の SELECT 列の補完。ProgressField 等と同じ仕組み)。
        /// SourceFields が空 (= 入力フィールド全部) のときはここでは列挙できない (モジュール定義が無い) ので何も足さない。
        /// </summary>
        public List<string> GetDependencyFields() => SourceFields.ToList();
    }
}
