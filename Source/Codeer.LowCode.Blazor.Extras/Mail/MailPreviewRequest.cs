namespace Codeer.LowCode.Blazor.Extras.Mail
{
    /// <summary>テンプレート展開で変数が入った区間 (解決後テキスト上の位置)。プレビューのハイライト用。</summary>
    public class MailTemplateSpan
    {
        public int Start { get; set; }
        public int Length { get; set; }

        /// <summary>テンプレート上の変数名 ("{Name.Value}" の中身)。</summary>
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// POST PreviewMailEndPoint (単発送信のプレビュー) のワイヤ形式。
    /// 文面はクライアントが自レコードで解決済み (送信時と同じ経路)。サーバーは差出人を解決して HTML を作る。
    /// </summary>
    public class MailPreviewRequest
    {
        public string MailInfraName { get; set; } = string.Empty;
        public MailMessage Message { get; set; } = new();

        /// <summary>解決前のテンプレート (プレビューのヘッダに出す)。</summary>
        public string SubjectTemplate { get; set; } = string.Empty;
        public string BodyTemplate { get; set; } = string.Empty;

        /// <summary>Message.Subject / Body 上の変数区間。</summary>
        public List<MailTemplateSpan> SubjectSpans { get; set; } = new();
        public List<MailTemplateSpan> BodySpans { get; set; } = new();

        /// <summary>プレビューの見出し (例: 送信元レコードのモジュール名と Id)。</summary>
        public string Title { get; set; } = string.Empty;
    }
}
