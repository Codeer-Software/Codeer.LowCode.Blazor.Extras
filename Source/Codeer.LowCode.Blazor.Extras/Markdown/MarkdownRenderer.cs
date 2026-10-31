using Markdig;
using Markdig.Extensions.GenericAttributes;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text.RegularExpressions;

namespace Codeer.LowCode.Blazor.Extras.Markdown
{
    /// <summary>
    /// MarkdownField の描画。DB や利用者から来る信頼できないテキストを描くので、Markdown 中の生 HTML は無効化する
    /// (エスケープされて文字として見える)。単独の改行は &lt;br&gt; にする (GitHub のコメントと同じ感覚で、
    /// 記法を知らなくても Enter が改行になる)。リンクは別タブで開く。リンクの URL は http / https / mailto / tel と相対、画像はそれに加えて data:image だけ (javascript: などは # にする)。
    /// 属性記法 ({onclick=...} 等) は任意の属性を出せるので使わない。
    /// </summary>
    public static class MarkdownRenderer
    {
        static readonly MarkdownPipeline _pipeline = CreatePipeline();

        static MarkdownPipeline CreatePipeline()
        {
            var builder = new MarkdownPipelineBuilder()
                .UseAdvancedExtensions()
                .UseSoftlineBreakAsHardlineBreak()
                .DisableHtml();
            builder.Extensions.RemoveAll(e => e is GenericAttributesExtension);
            return builder.Build();
        }

        static readonly Regex _anchor = new(@"<a\b([^>]*)>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hasTarget = new(@"\btarget\s*=", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex _hrefFragment = new(@"\bhref\s*=\s*[""']?#", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string ToHtml(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return string.Empty;
            var document = Markdig.Markdown.Parse(markdown, _pipeline);
            foreach (var link in document.Descendants<LinkInline>())
            {
                if (!IsSafeUrl(link.Url, link.IsImage)) link.Url = "#";
            }
            foreach (var link in document.Descendants<AutolinkInline>())
            {
                if (!IsSafeUrl(link.Url, false)) link.Url = "#";
            }
            using var writer = new StringWriter();
            var renderer = new HtmlRenderer(writer);
            _pipeline.Setup(renderer);
            renderer.Render(document);
            writer.Flush();
            return AddLinkTargets(writer.ToString());
        }

        //スキームが無い (相対・ページ内) か、http / https / mailto / tel (画像は data:image も) なら安全。ブラウザはスキームの中の空白・制御文字を無視するので除いてから見る
        static bool IsSafeUrl(string? url, bool isImage)
        {
            if (string.IsNullOrEmpty(url)) return true;
            var compact = new string(url.Where(c => c > ' ').ToArray());
            var colon = compact.IndexOf(':');
            if (colon < 0) return true;
            var beforeColon = compact[..colon];
            if (beforeColon.IndexOfAny(['/', '?', '#']) >= 0) return true;
            if (isImage && compact.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
            return beforeColon.Equals("http", StringComparison.OrdinalIgnoreCase)
                || beforeColon.Equals("https", StringComparison.OrdinalIgnoreCase)
                || beforeColon.Equals("mailto", StringComparison.OrdinalIgnoreCase)
                || beforeColon.Equals("tel", StringComparison.OrdinalIgnoreCase);
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
