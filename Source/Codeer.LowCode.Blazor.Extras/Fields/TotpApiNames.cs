namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>ログイン中の自分の認証アプリ (TOTP) の操作が共通の API (ExtrasTransport / Extras.Server の TotpApi) で使う機能名と操作名。</summary>
    public static class TotpApiNames
    {
        public const string Feature = "Totp";

        /// <summary>登録状態。要求なし、応答 TotpStatus。</summary>
        public const string Status = "Status";

        /// <summary>登録の解除。要求なし、応答 TotpStatus (アプリで TOTP が無効なら応答なし)。</summary>
        public const string Reset = "Reset";
    }
}
