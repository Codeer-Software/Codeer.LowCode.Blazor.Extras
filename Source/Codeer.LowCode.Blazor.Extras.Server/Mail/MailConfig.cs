namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// appsettings.json の "Mail" セクション = **製品 (共通層) が読む設定だけ**。
    /// プロバイダごとの設定 (SMTP / GraphApi / SendGrid / Gmail) はここには入らず、
    /// **それぞれ独立したセクションとして定義し、使う側 (テンプレートの対応表) が個別に読み出す**。
    /// プロバイダ間の差は <see cref="IMailSender"/> が吸収する。
    /// </summary>
    /// <remarks>
    /// 「どの送信先で送るか」の呼び名 (フィールドの MailInfraName / <c>MailSendRequest.MailInfraName</c>) を
    /// 実際の <see cref="IMailSender"/> に対応づけるのは**アプリのテンプレート側 (MailSenderTable)**。
    /// 製品はプロバイダ名も設定形式も知らないので、独自インフラも同じ対応表に足すだけで足りる。
    /// </remarks>
    public class MailConfig
    {
        /// <summary>
        /// ★デバッグ専用 (本番では設定しない)。設定すると、全メールが実際の宛先の代わりに
        /// このアドレスへリダイレクトされる。本番データのコピーで動く開発・ステージング環境で、
        /// 実在の宛先へ誤送信する事故を遮断するためのもの。
        /// 元の宛先は X-CLB-Original-To ヘッダに記録される。一斉送信は先頭10通に切り詰められる。
        /// </summary>
        public string DebugRedirectAllTo { get; set; } = string.Empty;

        /// <summary>
        /// 送信操作1回につき1行を記録する履歴モジュール名 (役割は MailHistoryContractField:
        /// SentAt / MailInfraName / Subject / TotalCount / SuccessCount / FailureDetails / SourceModule / SourceId)。
        /// 空 = 履歴なし。検証は実行時。履歴の異常が送信自体を失敗させることはない。
        /// </summary>
        public string HistoryModuleName { get; set; } = string.Empty;

        /// <summary>
        /// 単発送信で呼び名が省略されたときの既定 (例: "GraphApi")。
        /// この文字列を解釈するのはテンプレートの対応表なので、アプリが好きな呼び名を付けられる。
        /// フィールドの MailInfraName もこれも空なら**送信は「呼び名未指定」エラー**
        /// (空のまま対応表に渡して既定を推測させることはしない)。
        /// </summary>
        public string DefaultInfraName { get; set; } = string.Empty;

        /// <summary>
        /// 一斉送信で呼び名が省略されたときの既定 (例: "SendGrid")。空 = <see cref="DefaultInfraName"/>
        /// (それも空なら「呼び名未指定」エラー)。
        /// 「単発は通知系、一斉は配信サービス」の対をこの2つで固定する。
        /// </summary>
        public string DefaultBulkInfraName { get; set; } = string.Empty;
    }
}
