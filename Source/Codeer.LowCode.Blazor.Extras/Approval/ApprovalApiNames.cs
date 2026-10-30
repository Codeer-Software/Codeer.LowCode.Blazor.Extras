namespace Codeer.LowCode.Blazor.Extras.Approval
{
    /// <summary>承認フローが共通の API (ExtrasTransport / Extras.Server の ApprovalApi) で使う機能名と操作名。</summary>
    public static class ApprovalApiNames
    {
        public const string Feature = "Approval";

        /// <summary>承認の操作 (申請/再申請/承認/却下/差し戻し/取り下げ/確認。どれかは要求の Action)。要求 ApprovalCommand、応答 ApprovalActionResult。</summary>
        public const string Execute = "Execute";
    }
}
