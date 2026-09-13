using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// SemanticSearchField のランタイム。UI も読み込みデータも持たない。
    /// Submit のとき、対象フィールドのどれかが変更されていれば (新規なら常に) 行を文章にして送る。
    /// ベクトルはサーバー (SemanticSearchIndexer) が付ける。
    /// </summary>
    public class SemanticSearchField(SemanticSearchFieldDesign design) : FieldBase<SemanticSearchFieldDesign>(design)
    {
        /// <summary>対象フィールドのどれかが変更されているか (= この Submit で文章を送るか)。自前の状態は持たず対象フィールドに追従する。</summary>
        [ScriptHide]
        public override bool IsModified
            => Design.HasColumns && SemanticSearchText.SourceFields(Module.Design, Design).Any(n => Module.GetField(n)?.IsModified == true);

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
        {
            if (!Design.HasColumns || (!Module.IsNewData && !IsModified)) return new();
            var text = SemanticSearchText.Build(Services.AppInfoService.GetDesignData(), Module.Design, Module.GetData(), Design);
            return new() { FieldData = new SemanticSearchFieldData { Text = text } };
        }

        /// <summary>今の行を索引用の文章にしたもの (確認用。Submit で送られるのと同じ規則)。</summary>
        public string Text
            => Design.HasColumns ? SemanticSearchText.Build(Services.AppInfoService.GetDesignData(), Module.Design, Module.GetData(), Design) : string.Empty;
    }
}
