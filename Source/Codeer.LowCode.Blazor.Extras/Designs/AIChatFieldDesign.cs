using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// AI とのチャット UI。ユーザーの発言を送信し、サーバー (Agent) の返事を HTML で表示する。
    /// 通信は「送信 → requestId → ポーリング」(<see cref="AIChat.AIChatSendRequest"/> 等) で、
    /// 会話の履歴と AI の実体はサーバー側の持ち物。このフィールドは表示と入力だけを担う。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "ChatOutline")]
    [Designer(DisplayName = "$AIChatField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class AIChatFieldDesign() : FieldDesignBase(typeof(AIChatFieldDesign).FullName!), IFillHeightFieldDesign
    {
        /// <summary>
        /// 返事を作るサーバー側 Agent の名前。空なら既定の Agent。アプリがサーバーで登録した名前
        /// (例: "RawDataAccess" = DB を直接読んで集計とグラフで答える Agent) を指定し、同じフィールドで用途の違う Agent を使い分ける。
        /// </summary>
        [Designer]
        public string Agent { get; set; } = string.Empty;

        /// <summary>入力欄のプレースホルダ。</summary>
        [Designer]
        public string Placeholder { get; set; } = string.Empty;

        /// <summary>会話の先頭に表示するアシスタントの挨拶 (HTML 可)。空なら表示しない。</summary>
        [Designer(CandidateType = CandidateType.MultilineString)]
        public string WelcomeMessage { get; set; } = string.Empty;

        /// <summary>高さ (px)。0 なら親の高さに合わせる (FillAvailable のグリッドに置く前提)。</summary>
        [Designer]
        public int Height { get; set; }

        /// <summary>返事を待つ上限 (秒)。超えたらポーリングをやめてエラー表示にする (サーバーの処理は止めない)。</summary>
        [Designer]
        public int TimeoutSeconds { get; set; } = 600;

        /// <summary>入力欄が自動で伸びる上限の行数。</summary>
        [Designer]
        public int MaxInputRows { get; set; } = 6;

        /// <summary>返事が確定したときに呼ぶスクリプト。引数は返事の HTML。</summary>
        [Designer(CandidateType = CandidateType.ScriptEvent),
         ScriptMethod(ArgumentTypes = ["string"], ArgumentNames = ["replyHtml"])]
        public string OnReplyReceived { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(AIChatFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new AIChatField(this);
        public override FieldDataBase? CreateData() => null;
    }
}
