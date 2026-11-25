namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>メンションの候補 1 人 (RichTextField の候補の一覧の 1 行)。</summary>
    public class MentionCandidate
    {
        /// <summary>行の Id (ユーザーのモジュールの Id。本文の data-mention-id に入る)。</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>表示名 (空の人はログイン ID)。</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>ログイン ID (候補の補助の表示。項目を指定していなければ空)。</summary>
        public string LoginName { get; set; } = string.Empty;
    }

    /// <summary>メンションの候補の問い合わせの結果。</summary>
    public class MentionCandidates
    {
        /// <summary>候補の一覧に人がいるか (打った文字で絞る前)。いなければ「全員」も出さない。</summary>
        public bool HasPeople { get; set; }

        /// <summary>打った文字に合う候補 (一覧の並びのまま、先頭から)。</summary>
        public List<MentionCandidate> Items { get; set; } = new();
    }
}
