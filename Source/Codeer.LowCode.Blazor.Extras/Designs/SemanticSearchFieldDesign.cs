using Codeer.LowCode.Blazor.DataIO.Db;
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
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 行を「意味で探せる」ようにする書き込み専用フィールド (UI なし)。
    /// Submit のたびに <see cref="SourceFields"/> の値を「表示名: 値」の行に並べた文章を作り (クライアント側)、
    /// サーバーの SemanticSearchService がその文章の埋め込みベクトルを付けて、2 つの書き込み専用列に保存する。
    /// AI チャット (RawDataAccessAgent) は DB のベクトル検索 (pgvector / SQL Server 2025) でこの索引から「似た記録」を探す (search_records)。
    /// 距離計算は DB が行うので、ベクトル検索に対応しない DB (SQLite 等) のモジュールは意味検索の対象にならない。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TextSearch")]
    [Designer(DisplayName = "$SemanticSearchField")]
    [IgnoreBaseProperties(nameof(IgnoreModification), nameof(OnValidateInput), nameof(IsFocusSkip), nameof(OnFocusMoving), nameof(NextFocusField))]
    public class SemanticSearchFieldDesign() : FieldDesignBase(typeof(SemanticSearchFieldDesign).FullName!), IDataDependentField, ISqlMatchConditionFieldDesign
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeColumnsRequired = 1;
        private const int CodeFieldsNotLoaded = 2;
        private const int CodeSearchHasNoEffect = 3;
        private const int CodeSourceFieldsRestricted = 4;
        private const int CodeLinkTargetsRestricted = 5;

        /// <summary>文章にするフィールド (同じモジュール)。空なら DB 列を持つ入力フィールド全部 (Id・論理削除・楽観ロック・パスワード・作成/更新の記録は除く)。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "$SemanticSearchFieldSourceFields")]
        public List<string> SourceFields { get; set; } = [];

        /// <summary>行を文章にしたものを保存する列 (書き込み専用)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnText"), DbColumn(nameof(SemanticSearchFieldData.Text), IsWriteOnly = true)]
        public string DbColumnText { get; set; } = string.Empty;

        /// <summary>埋め込みベクトルを保存する列 (書き込み専用。<c>[0.1,-0.2,…]</c> の JSON 配列テキスト)。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnVector"), DbColumn(nameof(SemanticSearchFieldData.Vector), IsWriteOnly = true)]
        public string DbColumnVector { get; set; } = string.Empty;

        /// <summary>
        /// DB のベクトル検索 (pgvector / SQL Server 2025 の VECTOR 型) で距離計算に使うベクトル型の列 (必須)。
        /// アプリは <see cref="DbColumnVector"/> にテキスト (JSON 配列) で書くので、PostgreSQL ではそれをキャストする生成列の名前、
        /// SQL Server のようにテキストから VECTOR 型列へ直接書ける DB では <see cref="DbColumnVector"/> と同じ列名を設定する。
        /// </summary>
        [Designer(Index = 5, CandidateType = CandidateType.DbColumn, DisplayName = "$SemanticSearchFieldDbColumnVectorSearch")]
        public string DbColumnVectorSearch { get; set; } = string.Empty;

        /// <summary>文章の最大文字数 (超えた分は切り捨て。埋め込みモデルの入力上限の歯止め)。</summary>
        [Designer(Index = 6, DisplayName = "$SemanticSearchFieldMaxTextLength")]
        public int MaxTextLength { get; set; } = 8000;

        /// <summary>
        /// 検索レイアウトの検索欄で探すときのコサイン距離の上限 (0 = 同じ向き〜2)。これより遠い行は出さない。空なら距離では絞らない (全行を近い順に並べられるだけ)。
        /// </summary>
        [Designer(Index = 8, DisplayName = "$SemanticSearchFieldSearchMaxDistance")]
        public double? SearchMaxDistance { get; set; }

        /// <summary>スクリプトの Reindex / ReindexMissing で起こした再索引が終わった (成功・失敗・中断) ときに呼ぶスクリプト。結果は ReindexProcessed / ReindexError で見る。</summary>
        [Designer(Index = 7, DisplayName = "$SemanticSearchFieldOnReindexCompleted", CandidateType = CandidateType.ScriptEvent), ScriptMethod]
        public string OnReindexCompleted { get; set; } = string.Empty;

        /// <summary>
        /// 検索レイアウトにキーワード欄も出す。空白区切りの語をすべて含む行 (文章の列への部分一致の AND) に絞る。埋め込みを使わないので、
        /// ベクトル検索に対応しない DB のモジュールでも使える。文章 (意味検索) と一緒に入れると、絞ったうえで近い順になる。
        /// AI チャット (ModuleDataAccess の filters にこの項目の Like) と API (<see cref="SemanticMatchCondition.Keywords"/>) からは、この設定に関係なく使える。
        /// </summary>
        [Designer(Index = 9, DisplayName = "$SemanticSearchFieldKeywordSearch")]
        public bool KeywordSearch { get; set; }

        /// <summary>3 つの列 (文章 / ベクトル / ベクトル検索用) がすべて設定されているか (索引と検索の対象になる条件)。</summary>
        internal bool HasColumns => !string.IsNullOrWhiteSpace(DbColumnText) && !string.IsNullOrWhiteSpace(DbColumnVector) && !string.IsNullOrWhiteSpace(DbColumnVectorSearch);

        public override string GetWebComponentTypeFullName() => typeof(SemanticSearchFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => typeof(SemanticSearchSearchComponent).FullName!;
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
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = string.IsNullOrWhiteSpace(DbColumnText) ? nameof(DbColumnText) : string.IsNullOrWhiteSpace(DbColumnVector) ? nameof(DbColumnVector) : nameof(DbColumnVectorSearch) },
                    Message = Properties.Resources.SemanticSearchCheck_ColumnsRequired,
                });
            }
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnText), DbColumnText).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnVector), DbColumnVector).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnVectorSearch), DbColumnVectorSearch).AddTo(result);
            foreach (var field in SourceFields)
                context.CheckFieldFieldExistence(Name, nameof(SourceFields), field).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnReindexCompleted), OnReindexCompleted,
                context.GetScriptMethodAttribute(GetType(), nameof(OnReindexCompleted))).AddTo(result);
            result.AddRange(CheckSourceFieldsLoaded(context));
            result.AddRange(CheckSearchHasEffect(context));
            result.AddRange(CheckSourceFieldsReadRestriction(context));
            result.AddRange(CheckLinkTargetsReadRestriction(context));
            return result;
        }

        /// <summary>
        /// 文章の列は対象フィールドの値を全部含むので、このフィールドを読める人はキーワード検索 (部分一致を重ねる) で対象フィールドの値を言い当てられる。
        /// 対象フィールドに PermissionField の読み取り条件があるのに、このフィールド自身には無い構成を指摘する (このフィールドにも同じかそれより厳しい読み取り条件を付ける)。
        /// 厳しさの比較まではしない (有無だけ)。
        /// </summary>
        IEnumerable<DesignCheckInfo> CheckSourceFieldsReadRestriction(DesignCheckContext context)
        {
            var module = context.GetModuleDesign();
            if (module == null || !HasColumns) yield break;
            var rules = module.Fields.OfType<PermissionFieldDesign>().Where(p => p.ReadCondition.Condition != null).ToList();
            if (rules.Count == 0 || rules.Any(p => Targets(p, Name))) yield break;
            var restricted = SemanticSearchText.SourceFields(module, this).Where(s => rules.Any(p => Targets(p, s))).ToList();
            if (restricted.Count == 0) yield break;
            yield return new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), CodeSourceFieldsRestricted),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(SourceFields) },
                Message = string.Format(Properties.Resources.SemanticSearchCheck_SourceFieldsRestrictedFormat, string.Join(", ", restricted), Name),
            };
        }

        /// <summary>
        /// リンク (LinkField / SelectField) の表示名も文章に入る。リンク先モジュールに閲覧条件 (UserReadCondition) があると、本体はそのモジュールを読めないユーザーに
        /// 表示名を空で返すが、文章の列には保存時の表示名が残っているので、このフィールドを読める人はキーワード検索で言い当てられる。
        /// リンク先に閲覧条件があるのに、このフィールド自身に PermissionField の読み取り条件が無い構成を指摘する (:4 と同じく有無だけ)。
        /// </summary>
        IEnumerable<DesignCheckInfo> CheckLinkTargetsReadRestriction(DesignCheckContext context)
        {
            var module = context.GetModuleDesign();
            if (module == null || !HasColumns) yield break;
            if (module.Fields.OfType<PermissionFieldDesign>().Any(p => p.ReadCondition.Condition != null && Targets(p, Name))) yield break;
            var restricted = new List<string>();
            foreach (var name in SemanticSearchText.SourceFields(module, this))
            {
                var target = module.Fields.FirstOrDefault(f => f.Name == name) switch
                {
                    LinkFieldDesign link => link.SearchCondition.ModuleName,
                    SelectFieldDesign select => select.SearchCondition.ModuleName,
                    _ => null,
                };
                if (string.IsNullOrEmpty(target)) continue;
                if (context.DesignData.Modules.Find(target)?.UserReadCondition.Condition != null) restricted.Add($"{name} ({target})");
            }
            if (restricted.Count == 0) yield break;
            yield return new FieldDesignCheckInfo
            {
                Code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), CodeLinkTargetsRestricted),
                Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(SourceFields) },
                Message = string.Format(Properties.Resources.SemanticSearchCheck_LinkTargetsRestrictedFormat, string.Join(", ", restricted), Name),
            };
        }

        //PermissionField の対象にそのフィールドが入るか (除外モードは「指定した名前以外のすべて」。本体の実効対象の決め方と同じ)
        static bool Targets(PermissionFieldDesign rule, string fieldName)
            => rule.IsExcludeMode ? !rule.TargetFields.Contains(fieldName) : rule.TargetFields.Contains(fieldName);

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
        /// 検索レイアウトに置いたのに、文章を入れても一覧の見た目が変わらない構成を指摘する。距離の上限 (<see cref="SearchMaxDistance"/>) が空だと
        /// 索引のある行に絞られるだけなので、その検索レイアウトを使う一覧の並びにこのフィールドが無ければ、検索欄の文章は結果に効かない。
        /// 検索レイアウトを使う一覧は、ページの一覧 (PageFrame のリンク等)・SearchField の結果の一覧・LinkField の検索ダイアログ。
        /// </summary>
        IEnumerable<DesignCheckInfo> CheckSearchHasEffect(DesignCheckContext context)
        {
            var module = context.GetModuleDesign();
            if (module == null || SearchMaxDistance != null) yield break;
            var layouts = new HashSet<string>();
            foreach (var (layoutName, layout) in module.SearchLayouts)
            {
                try { if (layout.Layout.GetDescendantFields(module).Any(f => f.Name == Name)) layouts.Add(layoutName); }
                catch (InvalidOperationException) { } //存在しないフィールドがレイアウトにある = 別のチェックが指摘する
            }
            if (layouts.Count == 0) yield break;
            foreach (var (layoutName, owner, sorts) in SearchLayoutUsages(context.DesignData, module.Name))
            {
                if (!layouts.Contains(layoutName) || sorts.Any(e => !string.IsNullOrEmpty(e.Variable) && new VariableName(e.Variable).FieldName.FullName == Name)) continue;
                yield return new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(SemanticSearchFieldDesign), CodeSearchHasNoEffect),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(SearchMaxDistance) },
                    Message = string.Format(Properties.Resources.SemanticSearchCheck_SearchHasNoEffectFormat, layoutName, owner, Name),
                };
            }
        }

        //モジュールの検索レイアウトを使う一覧: (検索レイアウト名, 置き場所, 一覧の並び)
        static IEnumerable<(string Layout, string Owner, List<SortCondition> Sorts)> SearchLayoutUsages(DesignData design, string moduleName)
        {
            foreach (var frameName in design.PageFrames.GetPageFrameNames())
            {
                var frame = design.PageFrames.Find(frameName);
                if (frame == null) continue;
                var pages = frame.Header.Links.Concat(frame.Left.Links).Concat(frame.Right.Links).Cast<ModulePageDesign>()
                    .Concat(frame.OtherPageModuleDesigns).Append(frame.TopPageModuleDesign).OfType<ModulePageDesign>();
                //一覧を出すページ (詳細だけのページ以外。Auto / ListToDetail も Id 無しで開くと一覧)
                foreach (var page in pages.Where(e => e.Module == moduleName && e.ModulePageType != ModulePageType.Detail))
                {
                    if (page.ListPageDesign.ListFieldDesign is ListFieldDesignBase list)
                        yield return (page.ListPageDesign.SearchLayoutName, $"PageFrame {frameName}", Sorts(list.SearchCondition));
                }
            }
            foreach (var owner in design.Modules.ToList())
            {
                foreach (var search in owner.Fields.OfType<SearchFieldDesign>())
                {
                    if (owner.Fields.FirstOrDefault(f => f.Name == search.ResultsViewFieldName) is ListFieldDesignBase list && list.SearchCondition.ModuleName == moduleName)
                        yield return (search.LayoutName, $"{owner.Name}.{search.Name}", Sorts(list.SearchCondition));
                }
                foreach (var link in owner.Fields.OfType<LinkFieldDesign>().Where(e => e.SearchCondition.ModuleName == moduleName))
                    yield return (link.SearchLayoutName, $"{owner.Name}.{link.Name}", Sorts(link.SearchCondition));
            }
        }

        static List<SortCondition> Sorts(SearchCondition condition)
        {
#pragma warning disable CS0618 // 旧形式の並び指定 (SortFieldVariable) も見る
            return condition.SortConditions.Append(new SortCondition { Variable = condition.SortFieldVariable }).ToList();
#pragma warning restore CS0618
        }

        /// <summary>
        /// このフィールドがレイアウトか DataOnlyFields にあれば、文章にするフィールドも一緒に読み込む (本体の SELECT 列の補完。ProgressField 等と同じ仕組み)。
        /// SourceFields が空 (= 入力フィールド全部) のときはここでは列挙できない (モジュール定義が無い) ので何も足さない。
        /// </summary>
        public List<string> GetDependencyFields() => SourceFields.ToList();

        /// <summary>
        /// 意味検索の条件 (<see cref="SemanticMatchCondition"/>) を WHERE にする: ベクトルがあれば索引のある行 (+ 距離の上限。距離計算は DB = pgvector / SQL Server 2025)、
        /// キーワードがあれば文章の列が語をすべて含む行 (語ごとの LIKE の AND。どの DB でも動く)。両方あれば AND。
        /// 本体の一覧検索がこの断片を自分の WHERE に入れるので、行の条件・論理削除・権限はそのまま効く。
        /// </summary>
        public string CreateWhere(FieldSqlMatchCondition condition, SqlConditionContext context)
        {
            var semantic = ToSemantic(condition);
            var parts = new List<string>();
            if (semantic.Vector.Length > 0)
            {
                parts.Add($"{context.Column(DbColumnVectorSearch)} is not null");
                if (semantic.MaxDistance != null) parts.Add($"{Distance(semantic, context)} <= {context.AddParameter(semantic.MaxDistance.Value)}");
            }
            //語ごとの部分一致 (パターン・エスケープは本体の Like と同じ)
            foreach (var term in SemanticSearchKeywords.Split(semantic.Keywords))
                parts.Add(context.Like(DbColumnText, term));
            return parts.Count == 1 ? parts[0] : $"({string.Join(" and ", parts)})";
        }

        /// <summary>近い順 (コサイン距離の昇順) に並べる式。キーワードだけの条件 (ベクトル無し) では並べる基準が無いので null。</summary>
        public string? CreateOrderBy(FieldSqlMatchCondition condition, SqlConditionContext context)
        {
            var semantic = ToSemantic(condition);
            return semantic.Vector.Length == 0 ? null : Distance(semantic, context);
        }

        SemanticMatchCondition ToSemantic(FieldSqlMatchCondition condition)
        {
            if (condition is not SemanticMatchCondition semantic || !HasColumns)
                throw LowCodeException.Create(Properties.Resources.SemanticSearch_ConditionNotSupported, Name);
            if (semantic.Vector.Length == 0)
            {
                //文章があるのにベクトルが無い = サーバーの前処理 (ConditionInterceptor) が結線されていない
                if (!string.IsNullOrWhiteSpace(semantic.Text)) throw LowCodeException.Create(Properties.Resources.SemanticSearch_VectorRequired, Name);
                if (SemanticSearchKeywords.Split(semantic.Keywords).Count == 0) throw LowCodeException.Create(Properties.Resources.SemanticSearch_ConditionEmpty, Name);
            }
            return semantic;
        }

        //コサイン距離の式。ベクトルはパラメータで渡す (文字列で埋め込むと検索のたびに SQL 文が変わり、DB が毎回プランを作り直す)
        string Distance(SemanticMatchCondition condition, SqlConditionContext context)
        {
            var type = context.DataSourceType;
            if (!SemanticSearchVector.SupportsDbSearch(type)) throw LowCodeException.Create(Properties.Resources.SemanticSearch_DbNotSupported, type.ToString());
            var vector = SemanticSearchVector.FromText(type, context.AddParameter(SemanticSearchVector.Encode(condition.Vector)), condition.Vector.Length);
            return SemanticSearchVector.CosineDistance(type, context.Column(DbColumnVectorSearch), vector);
        }
    }
}
