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
    /// 高さは置いた場所に従う (IsFillAvailable のグリッドの最終行なら残り全部、行に Height があればその高さ、普通の行なら会話の領域が 16rem)。
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
        [Designer(Index = 0, DisplayName = "$AIChatFieldAgent")]
        public string Agent { get; set; } = string.Empty;

        /// <summary>
        /// この会話に渡す補足文書のフォルダ (デザインプロジェクトの Resources からの相対パス。例: "AIChat/Sales")。
        /// そのフォルダの Markdown / テキストが業務用語の定義や集計の決まりとして Agent に渡る。空なら文書なし。
        /// チャットごとに別のフォルダを指定して、用途に合った文書だけを渡す (トークンの節約にもなる)。
        /// </summary>
        [Designer(Index = 1, DisplayName = "$AIChatFieldDocumentFolder")]
        public string DocumentFolder { get; set; } = string.Empty;

        /// <summary>
        /// 画面を離れても会話を保持するか (既定 true)。会話はアプリのメモリ (WASM ならブラウザのタブ) に置かれ、
        /// 返事の中のリンクで別ページへ行って戻ってきても消えず、待ち中だった返事も届く。リロードか「新しい会話」で消える。
        /// false なら画面を離れた時点で会話は消え、待ち中の返事は中断する。
        /// </summary>
        [Designer(Index = 2, DisplayName = "$AIChatFieldKeepConversation")]
        public bool KeepConversation { get; set; } = true;

        /// <summary>
        /// Enter キーで送信するか (既定 true)。false なら Enter は改行になる。
        /// Shift+Enter は常に改行、Ctrl+Enter は常に送信 (この 2 つは設定に関係なく固定)。
        /// </summary>
        [Designer(Index = 3, DisplayName = "$AIChatFieldSendOnEnter")]
        public bool SendOnEnter { get; set; } = true;

        /// <summary>入力欄の行数 (最小)。既定 3 で「複数行を書ける」と分かる高さにする。内容が増えれば 12 行まで自動で伸び、それ以上は入力欄の中でスクロール。</summary>
        [Designer(Index = 4, DisplayName = "$AIChatFieldMinInputRows")]
        public int MinInputRows { get; set; } = 3;

        /// <summary>返事を待つ上限 (秒)。超えたらポーリングをやめてエラー表示にする (サーバーの処理は止めない)。</summary>
        [Designer(Index = 5, DisplayName = "$AIChatFieldTimeoutSeconds")]
        public int TimeoutSeconds { get; set; } = 600;

        /// <summary>返事が確定したときに呼ぶスクリプト。引数は返事の HTML。</summary>
        [Designer(Index = 6, DisplayName = "$AIChatFieldOnReplyReceived", CandidateType = CandidateType.ScriptEvent),
         ScriptMethod(ArgumentTypes = ["string"], ArgumentNames = ["replyHtml"])]
        public string OnReplyReceived { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(AIChatFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new AIChatField(this);
        public override FieldDataBase? CreateData() => null;
    }
}
