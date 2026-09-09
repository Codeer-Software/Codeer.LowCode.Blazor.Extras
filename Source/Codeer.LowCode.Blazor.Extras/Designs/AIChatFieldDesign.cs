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

        /// <summary>
        /// この会話に渡す補足文書のフォルダ (デザインプロジェクトの Resources からの相対パス。例: "AIChat/Sales")。
        /// そのフォルダの Markdown / テキストが業務用語の定義や集計の決まりとして Agent に渡る。空なら文書なし。
        /// チャットごとに別のフォルダを指定して、用途に合った文書だけを渡す (トークンの節約にもなる)。
        /// </summary>
        [Designer]
        public string DocumentFolder { get; set; } = string.Empty;

        /// <summary>高さ (px)。0 なら親の高さに合わせる (FillAvailable の最終行では残り全部、普通の行では会話の領域が 16rem。返事が増えても行は伸びない)。</summary>
        [Designer]
        public int Height { get; set; }

        /// <summary>返事を待つ上限 (秒)。超えたらポーリングをやめてエラー表示にする (サーバーの処理は止めない)。</summary>
        [Designer]
        public int TimeoutSeconds { get; set; } = 600;

        /// <summary>入力欄が自動で伸びる上限の行数。</summary>
        [Designer]
        public int MaxInputRows { get; set; } = 6;

        /// <summary>
        /// Enter キーで送信するか (既定 true)。false なら Enter は改行になる。
        /// Shift+Enter は常に改行、Ctrl+Enter は常に送信 (この 2 つは設定に関係なく固定)。
        /// </summary>
        [Designer]
        public bool SendOnEnter { get; set; } = true;

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
