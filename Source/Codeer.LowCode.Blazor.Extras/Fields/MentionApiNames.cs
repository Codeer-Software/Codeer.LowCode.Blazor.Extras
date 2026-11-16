namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>RichTextField のメンションが共通の API (ExtrasTransport / Extras.Server の MentionApi) で使う機能名と操作名。</summary>
    public static class MentionApiNames
    {
        public const string Feature = "Mention";

        /// <summary>打った文字に合う候補。要求 MentionCandidatesRequest、応答 MentionCandidatesResponse。</summary>
        public const string Candidates = "Candidates";
    }

    /// <summary>メンションの候補の問い合わせ (共通の API の MentionApiNames.Candidates)。</summary>
    public class MentionCandidatesRequest
    {
        /// <summary>RichTextField を持つモジュール。</summary>
        public string ModuleName { get; set; } = string.Empty;

        /// <summary>RichTextField の名前。</summary>
        public string FieldName { get; set; } = string.Empty;

        /// <summary>@ の後ろに打った文字 (空なら絞らない)。</summary>
        public string Text { get; set; } = string.Empty;
    }

    /// <summary>メンションの候補の問い合わせの応答。</summary>
    public class MentionCandidatesResponse
    {
        /// <summary>候補 (表示名の昇順)。</summary>
        public List<MentionCandidate> Candidates { get; set; } = new();
    }

    /// <summary>メンションの候補 1 人。</summary>
    public class MentionCandidate
    {
        /// <summary>ログインユーザーのモジュールの行の Id。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>表示名 (ログインアカウント契約の DisplayName。空ならログイン ID)。</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>ログイン ID (候補の補助の表示)。</summary>
        public string LoginName { get; set; } = string.Empty;
    }
}
