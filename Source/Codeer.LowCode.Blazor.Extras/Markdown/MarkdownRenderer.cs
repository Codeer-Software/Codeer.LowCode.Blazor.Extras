using Markdig;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Markdown
{
    /// <summary>
    /// MarkdownField の描画。DB や利用者から来る信頼できないテキストを描くので、Markdown 中の生 HTML は無効化する
    /// (エスケープされて文字として見える)。単独の改行は &lt;br&gt; にする (GitHub のコメントと同じ感覚で、
    /// 記法を知らなくても Enter が改行になる)。リンクは別タブで開く。
    /// </summary>
    public static class MarkdownRenderer
    {
        static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseSoftlineBreakAsHardlineBreak()
            .DisableHtml()
            .Build();

        static readonly Regex _anchor = new(@"<a\b([^>]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hasTarget = new(@"\btarget\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hrefFragment = new(@"\bhref\s*=\s*[""']?#", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ToHtml(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            return AddLinkTargets(Markdig.Markdown.ToHtml(markdown, _pipeline));
        }

        /// <summary>一覧セルなどの 1 行表示用に、記法を落としたプレーンテキストにする。</summary>
        public static string ToPlainText(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            return Markdig.Markdown.ToPlainText(markdown, _pipeline).Trim();
        }

        static string AddLinkTargets(string html)
            => _anchor.Replace(html, m =>
            {
                var attrs = m.Groups[1].Value;
                if (_hasTarget.IsMatch(attrs) || _hrefFragment.IsMatch(attrs)) return m.Value;
                return $"<a{attrs} target=\"_blank\" rel=\"noopener noreferrer\">";
            });
    }
}
