using System.Net;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// RichTextField の値 (HTML) に入っているメンションの読み取り。
    /// メンションは &lt;span class="mention" data-mention-id="Id"&gt;@名前&lt;/span&gt; (人) と &lt;span class="mention" data-mention-all=""&gt;@全員&lt;/span&gt; (全員)。
    /// 値はブラウザが書いた HTML なので、属性の順や引用符の種類に依らず span の開始タグの属性を見る。
    /// </summary>
    internal static class MentionHtml
    {
        static readonly Regex SpanTag = new(@"<span\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex MentionId = new(@"\sdata-mention-id\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s""'>]+))", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex MentionAll = new(@"\sdata-mention-all(?=[\s=/>])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>メンションした人の Id (重複を除き、本文の順)。</summary>
        internal static string[] GetMentionedIds(string? html)
        {
            if (string.IsNullOrEmpty(html)) return [];
            var ids = new List<string>();
            foreach (Match tag in SpanTag.Matches(html))
            {
                var match = MentionId.Match(tag.Value);
                if (!match.Success) continue;
                var id = WebUtility.HtmlDecode(match.Groups["v"].Value);
                if (id.Length > 0 && !ids.Contains(id)) ids.Add(id);
            }
            return ids.ToArray();
        }

        /// <summary>「全員」が入っているか。</summary>
        internal static bool MentionsAll(string? html)
            => !string.IsNullOrEmpty(html) && SpanTag.Matches(html).Any(tag => MentionAll.IsMatch(tag.Value));
    }
}
