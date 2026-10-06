using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

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
    /// タグを入力・表示するフィールド。タグはテーブルに持つ (CLB の多対多の形):
    /// タグのマスタ (TagContractField) を 1 つと、タグを付けるモジュールごとのタグ付けモジュール (TagLinkContractField)。
    /// このフィールドはタグ付けモジュールを子の一覧として持つ一覧フィールドで、検索条件 (SearchCondition) でタグ付けモジュールと
    /// 「OwnerId.Value = Id.Value」の結び付きを指定する (タグのセットアップが作る)。読み込み・保存・子のパスでの検索は本体の一覧と同じ。
    /// 検索条件のモジュールが空なら保存しない入力欄になり、候補は TagModuleName のマスタから出す (取り込み画面・一括でタグを付ける画面用)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "TagMultiple")]
    [Designer(DisplayName = "$TagField")]
    [IgnoreBaseProperties(nameof(IsInMemoryPaging), nameof(ConfirmBeforePageChange), nameof(PagerPosition), nameof(ReplaceMode),
        nameof(DeleteTogether), nameof(CanCreate), nameof(CanUpdate), nameof(CanDelete), nameof(CanUserSort), nameof(CanSelect),
        nameof(UseIndexSort), nameof(ConfirmBeforeDelete), nameof(OnSelectedIndexChanged), nameof(OnSelectedIndexChanging),
        nameof(OnDoubleClickRow), nameof(IgnoreModification))]
    public class TagFieldDesign : ListFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeNotLinkModule = 1;
        private const int CodeOwnerBindingMissing = 2;
        private const int CodeLinkLayoutFieldsMissing = 3;
        private const int CodeTagModuleRequired = 4;
        private const int CodeTagModuleNotMaster = 5;
        private const int CodeTagModuleMismatch = 6;

        public TagFieldDesign() : base(typeof(TagFieldDesign).FullName!)
        {
            //タグ付け行はレコードの一部: レコードを消せば一緒に消す (マスタへの FK があるので残すと消せない)。行の追加・削除は確認なし。
            //CanUpdate が false だと本体の一覧は閲覧のみ (IsViewOnly) になるので true (タグ付け行そのものを書き換えることはない)
            DeleteTogether = true;
            ConfirmBeforeDelete = false;
            CanCreate = true;
            CanUpdate = true;
            CanDelete = true;
        }

        /// <summary>タグ付けモジュールの行を読むときの一覧レイアウト (タグ付けモジュールの既定の一覧。OwnerId と Tag を DataOnlyFields に入れる)。</summary>
        public override string LayoutName { get; set; } = string.Empty;

        /// <summary>タグのマスタ (TagContractField を置いたモジュール)。結び付きありなら空でよい (タグ付けモジュールのリンクの先)。結び付きなしでは必須。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Module, DisplayName = "$TagFieldTagModuleName")]
        public string TagModuleName { get; set; } = string.Empty;

        [Designer(Index = 4, DisplayName = "$TagFieldPlaceholder")]
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>スペースでもタグを確定する。既定は Enter と「,」「、」だけ (日本語のタグにはスペースが入ることがあるため)。</summary>
        [Designer(Index = 5, DisplayName = "$TagFieldConfirmOnSpace")]
        public bool ConfirmOnSpace { get; set; }

        /// <summary>マスタに無いタグを入力できる (足した時点でマスタに行を作る)。false ならマスタにあるタグだけ。</summary>
        [Designer(Index = 6, DisplayName = "$TagFieldAllowNewTags")]
        public bool AllowNewTags { get; set; } = true;

        [Designer(Index = 70, DisplayName = "$IsRequired")]
        public bool IsRequired { get; set; }

        /// <summary>検索欄に一致 (すべて含む / いずれかを含む) の選択を出さない (既定の一致で検索する)。</summary>
        [Designer(Index = 0, DisplayName = "$IsSimpleSearchParameter", Category = "$SearchSettings")]
        public bool IsSimpleSearchParameter { get; set; }

        /// <summary>検索画面の一致の既定 (画面で切り替えられる)。</summary>
        [Designer(Index = 1, DisplayName = "$TagFieldSearchMatchDefaultValue", Category = "$SearchSettings")]
        public TagSearchMatch SearchMatchDefaultValue { get; set; } = TagSearchMatch.All;

        /// <summary>
        /// 検索で打った文字を含むタグをすべて対象にする (「展示会」で「展示会2026」も)。既定は丸ごと一致。
        /// 打った文字ごとにマスタを 1 回引いてタグ Id にしてから探すので、タグが付いている人の数には影響されない。
        /// </summary>
        [Designer(Index = 2, DisplayName = "$TagFieldPartialMatch", Category = "$SearchSettings")]
        public bool PartialMatch { get; set; }

        public override string GetWebComponentTypeFullName() => typeof(TagFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => typeof(TagFieldSearchComponent).FullName!;

        //デザイナで固定の検索条件にタグを書く編集は無い (タグは画面の検索欄で選ぶ)
        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new TagField(this);

        public override FieldDataBase CreateData() => new ListFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            //モジュール・変数・レイアウトの存在は基底 (一覧) が見る
            var result = base.CheckDesign(context);
            context.CheckFieldName(Name).AddTo(result);
            if (!string.IsNullOrEmpty(TagModuleName)) context.CheckFieldModuleExistence(Name, nameof(TagModuleName), TagModuleName).AddTo(result);

            var linkModuleName = SearchCondition?.ModuleName ?? string.Empty;
            if (string.IsNullOrEmpty(linkModuleName))
            {
                //結び付きなし: マスタが必須
                if (string.IsNullOrEmpty(TagModuleName))
                    result.Add(Error(context, CodeTagModuleRequired, nameof(TagModuleName), Properties.Resources.TagCheck_TagModuleRequired));
                else
                    CheckMaster(context, result, TagModuleName, nameof(TagModuleName));
                return result;
            }

            var linkModule = context.DesignData.Modules.Find(linkModuleName);
            if (linkModule == null) return result; //不在は基底が指摘する
            var link = TagContracts.LinkContract(linkModule);
            if (link == null)
            {
                result.Add(Error(context, CodeNotLinkModule, nameof(SearchCondition), string.Format(Properties.Resources.TagCheck_NotLinkModuleFormat, linkModuleName)));
                return result;
            }

            //行をこのレコードに絞る結び付き (OwnerId.Value = 本体の変数)
            if (string.IsNullOrEmpty(TagContracts.OwnerKeyVariable(SearchCondition!, link.OwnerId)))
                result.Add(Error(context, CodeOwnerBindingMissing, nameof(SearchCondition), string.Format(Properties.Resources.TagCheck_OwnerBindingMissingFormat, link.OwnerId, linkModuleName)));

            //行の OwnerId と Tag が読まれること (レイアウトに無いフィールドは値が空で届く)
            if (linkModule.ListLayouts.TryGetValue(LayoutName, out var layout))
            {
                var loaded = layout.Elements.SelectMany(e => e).Select(e => e.FieldName).Concat(layout.DataOnlyFields).ToHashSet();
                var missing = new[] { link.OwnerId, link.Tag }.Where(e => !string.IsNullOrEmpty(e) && !loaded.Contains(e)).ToList();
                if (missing.Count > 0)
                    result.Add(Error(context, CodeLinkLayoutFieldsMissing, nameof(SearchCondition), string.Format(Properties.Resources.TagCheck_LinkLayoutFieldsMissingFormat, LayoutName, linkModuleName, string.Join(", ", missing))));
            }

            //マスタ: タグ付けモジュールのリンクの先 (TagModuleName を書くなら同じもの)
            var master = (linkModule.Fields.FirstOrDefault(e => e.Name == link.Tag) as LinkFieldDesign)?.SearchCondition.ModuleName ?? string.Empty;
            if (!string.IsNullOrEmpty(TagModuleName) && !string.IsNullOrEmpty(master) && TagModuleName != master)
                result.Add(Error(context, CodeTagModuleMismatch, nameof(TagModuleName), string.Format(Properties.Resources.TagCheck_TagModuleMismatchFormat, TagModuleName, master, linkModuleName)));
            return result;
        }

        void CheckMaster(DesignCheckContext context, List<DesignCheckInfo> result, string moduleName, string member)
        {
            var module = context.DesignData.Modules.Find(moduleName);
            if (module == null || TagContracts.MasterContract(module) != null) return;
            result.Add(Error(context, CodeTagModuleNotMaster, member, string.Format(Properties.Resources.TagCheck_NotTagMasterFormat, moduleName)));
        }

        FieldDesignCheckInfo Error(DesignCheckContext context, int code, string member, string message) => new()
        {
            Code = DesignCheckCode.Create(typeof(TagFieldDesign), code),
            Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = member },
            Message = message,
        };

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddModule(TagModuleName, x => TagModuleName = x)
            .Build();
    }
}
