using Codeer.LowCode.Blazor.Designer.Extensibility;
using System.IO;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Designer
{
    /// <summary>
    /// フィールドを持たない機能 (監査ログ) の AI 用仕様ドキュメント。埋め込み (<c>Codeer.LowCode.Blazor.Extras.Designer.SpecDocs.&lt;名前&gt;.md</c>) を
    /// <see cref="SpecDocCatalog"/> に登録し、headless の ai-refresh が <c>_specs/&lt;名前&gt;.md</c> として書き出す。
    /// フィールドカタログ (FieldDocs) に載らない機能を Claude Code が知る経路はこれだけ。
    /// </summary>
    public static class ExtrasSpecDocs
    {
        internal const string Prefix = "Codeer.LowCode.Blazor.Extras.Designer.SpecDocs.";

        static readonly Assembly DocAsm = typeof(ExtrasSpecDocs).Assembly;

        /// <summary>埋め込みの SpecDocs をすべて登録する (冪等。同じ id は後勝ちで同じ内容)。</summary>
        public static void Register()
        {
            foreach (var name in GetResourceNames())
                SpecDocCatalog.RegisterEmbedded(DocAsm, name);
        }

        /// <summary>埋め込まれている SpecDocs のリソース名 (id 順)。</summary>
        public static IReadOnlyList<string> GetResourceNames()
            => DocAsm.GetManifestResourceNames()
                .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        /// <summary>埋め込みの SpecDoc を読む (無ければ null)。</summary>
        public static string? Load(string id)
        {
            using var stream = DocAsm.GetManifestResourceStream(Prefix + id + ".md");
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
