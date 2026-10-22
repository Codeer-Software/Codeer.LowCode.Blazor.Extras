using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>TagField の検索で、選んだタグをどう組み合わせるか。</summary>
    public enum TagSearchMatch
    {
        /// <summary>選んだタグをすべて含む行。</summary>
        [Designer(DisplayName = "$TagSearchMatch_All")] All,
        /// <summary>選んだタグのどれかを含む行。</summary>
        [Designer(DisplayName = "$TagSearchMatch_Any")] Any,
    }

    /// <summary>
    /// タグを入力・表示して保存するフィールド。タグはタグ付けモジュール (TagLinkContractField。タグを付けるモジュールごとに 1 つ) の行に持つ:
    /// 1 行 = このレコードに付いたタグ 1 つ (OwnerId + タグ名)。タグのマスタは無い。
    /// 結び付き (OwnerId.Value = Id.Value・並び Id・読む列) はタグ付けモジュールの契約から組み立てて本体に渡す
    /// (検索・一覧の行への同梱・子のパスの解決・レコードの削除で一緒に消す DeleteTogether)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TagMultiple")]
    [Designer(DisplayName = "$TagField")]
    public class TagFieldDesign() : FieldDesignBase(typeof(TagFieldDesign).FullName!), IDisplayName, IChildRecordsFieldDesign, IOwnedRecordsFieldDesign, ISemanticSearchTextSource, IAllowOverflowFieldDesign
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeNotLinkModule = 1;
        private const int CodeLinkModuleCannotDelete = 2;

        [Designer(DisplayName = "$DisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>タグ付けモジュール (TagLinkContractField を置いたモジュール)。タグのセットアップが入れる。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Module, DisplayName = "$TagFieldTagModuleName")]
        public string TagModuleName { get; set; } = string.Empty;

        [Designer(Index = 4, DisplayName = "$TagFieldPlaceholder")]
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>
        /// レコードの削除でタグ付け行も一緒に消す (本体の DeleteTogether = 従属レコードの宣言。親の削除の前にタグ付け行を消す。タグ付けモジュールの CanDelete が要る)。
        /// false なら本体は消さない (DDL の外部キー ON DELETE CASCADE に任せる・論理削除の親のタグ付け行を残して復活でそのまま戻すとき)。
        /// </summary>
        [Designer(Index = 5, DisplayName = "$DeleteTogether")]
        public bool DeleteTogether { get; set; } = true;

        /// <summary>検索欄に一致 (すべて含む / いずれかを含む) の選択を出さない (既定の一致で検索する)。</summary>
        [Designer(Index = 0, DisplayName = "$IsSimpleSearchParameter", Category = "$SearchSettings")]
        public bool IsSimpleSearchParameter { get; set; }

        /// <summary>検索画面の一致の既定 (画面で切り替えられる)。</summary>
        [Designer(Index = 1, DisplayName = "$TagFieldSearchMatchDefaultValue", Category = "$SearchSettings")]
        public TagSearchMatch SearchMatchDefaultValue { get; set; } = TagSearchMatch.All;

        [Designer(CandidateType = CandidateType.ScriptEvent, DisplayName = "$OnDataChanged")]
        public string OnDataChanged { get; set; } = string.Empty;

        /// <summary>
        /// このレコードのタグ付け行: タグ付けモジュールの「OwnerId.Value = Id.Value」を付けた順 (Id) に、Id・OwnerId・タグ名を読む。
        /// 役割の名前は契約から読む。タグ付けモジュールが無い・契約が無いときは null (結び付きが決まらない = 子レコードを持たない。デザインチェックが指摘する)。
        /// </summary>
        public SearchCondition? GetChildRecordsCondition(IModuleDesigns modules)
        {
            var link = TagContracts.LinkContract(modules.Find(TagModuleName));
            if (link == null) return null;
            return new SearchCondition(TagModuleName)
            {
                Condition = MultiMatchCondition.And(new FieldVariableMatchCondition
                {
                    SearchTargetVariable = $"{link.OwnerId}.Value",
                    Comparison = MatchComparison.Equal,
                    Variable = $"{SystemFieldNames.Id}.Value",
                }),
                SortConditions = [new SortCondition { Variable = $"{SystemFieldNames.Id}.Value" }],
                SelectFields = [SystemFieldNames.Id, link.OwnerId, link.TagName],
            };
        }

        //従属レコードの宣言: タグ付け行はレコードの一部 (編集履歴の版に入り、復元で戻る。DeleteTogether ならレコードの削除で本体が消す)。契約が無ければ宣言しない
        public IEnumerable<OwnedRecordsDesign> GetOwnedRecords(IModuleDesigns modules)
        {
            var condition = GetChildRecordsCondition(modules);
            if (condition == null) yield break;
            yield return new OwnedRecordsDesign { Name = Name, Condition = condition, HoldsAllRecords = true, DeleteTogether = DeleteTogether };
        }

        //意味検索の文章: 付いているタグ名を「, 」で並べる (無ければ null = 文章に入れない)
        public string? GetSemanticText(DesignData? design, FieldDataBase data)
        {
            if (data is not ListFieldData rows) return null;
            var names = TagContracts.TagNames(design, this, rows);
            return names.Count == 0 ? null : string.Join(", ", names);
        }

        public override string GetWebComponentTypeFullName() => typeof(TagFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => typeof(TagFieldSearchComponent).FullName!;

        //デザイナで固定の検索条件にタグを書く編集は無い (タグは画面の検索欄で選ぶ)
        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new TagField(this);

        public override FieldDataBase CreateData() => new ListFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldModuleExistence(Name, nameof(TagModuleName), TagModuleName).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged, null).AddTo(result);

            var linkModule = context.DesignData.Modules.Find(TagModuleName);
            if (string.IsNullOrEmpty(TagModuleName) || (linkModule != null && TagContracts.LinkContract(linkModule) == null))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TagFieldDesign), CodeNotLinkModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(TagModuleName) },
                    Message = string.Format(Properties.Resources.TagCheck_NotLinkModuleFormat, TagModuleName),
                });
            }
            //本体は親の削除でタグ付け行を消すときタグ付けモジュールの CanDelete を見る (無ければ親の削除ごと失敗する)。契約が無ければ宣言しないので :1 だけ
            if (DeleteTogether && linkModule != null && TagContracts.LinkContract(linkModule) != null && !linkModule.CanDelete)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TagFieldDesign), CodeLinkModuleCannotDelete),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(DeleteTogether) },
                    Message = string.Format(Properties.Resources.TagCheck_LinkModuleCannotDeleteFormat, TagModuleName),
                });
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(TagModuleName, value => TagModuleName = value)
            .Build();
    }
}
