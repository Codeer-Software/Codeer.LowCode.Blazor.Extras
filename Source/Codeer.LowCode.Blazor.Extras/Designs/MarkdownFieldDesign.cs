using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>MarkdownField の編集画面でのプレビューの出し方。</summary>
    public enum MarkdownPreviewMode
    {
        /// <summary>「編集」「プレビュー」のタブで切り替える。</summary>
        [Designer(DisplayName = "$MarkdownPreviewMode_Tab")] Tab,
        /// <summary>左に編集、右にプレビューを並べる。</summary>
        [Designer(DisplayName = "$MarkdownPreviewMode_Split")] Split,
        /// <summary>プレビューを出さない (閲覧時だけ描画)。</summary>
        [Designer(DisplayName = "$MarkdownPreviewMode_None")] None,
    }

    /// <summary>
    /// Markdown を入力・表示する値フィールド。値はプレーンテキスト (Markdown) のまま DB 列に保存し、
    /// 閲覧時 (IsViewOnly) は HTML に描画して表示する。RichTextField (HTML を保存する WYSIWYG) とは
    /// 「書き手が記法を知っている人か AI か」で使い分ける。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "LanguageMarkdown")]
    [Designer(DisplayName = "$MarkdownField")]
    public class MarkdownFieldDesign() : ValueFieldDesignBase(typeof(MarkdownFieldDesign).FullName!)
    {
        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$MarkdownFieldDbColumn"), DbColumn(nameof(MarkdownFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        [Designer(Index = 1, DisplayName = "$MarkdownFieldPlaceholder")]
        public string Placeholder { get; set; } = string.Empty;

        //高さは持たない: 編集欄は中身に合わせて伸び、FillAvailable の最終行では残り高さいっぱい、
        //固定したいときは Grid の行 Height で (RichTextField と同じ。レイアウトの責務)

        /// <summary>最大文字数 (Markdown の文字数)。null なら制限なし。</summary>
        [Designer(Index = 3, DisplayName = "$MarkdownFieldMaxLength")]
        public int? MaxLength { get; set; }

        [Designer(Index = 4, DisplayName = "$MarkdownFieldPreviewMode")]
        public MarkdownPreviewMode PreviewMode { get; set; } = MarkdownPreviewMode.Tab;

        /// <summary>見出し・太字・リスト・リンク・表などを挿入するツールバーを出す。</summary>
        [Designer(Index = 5, DisplayName = "$MarkdownFieldShowToolbar")]
        public bool ShowToolbar { get; set; } = true;

        public override string GetWebComponentTypeFullName() => typeof(MarkdownFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new MarkdownField(this);

        public override FieldDataBase? CreateData() => new MarkdownFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            return result;
        }
    }
}
