using System.IO;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Designer
{
    /// <summary>
    /// Extras 系スクリプトオブジェクト(Excel / WebApi / Toaster / Mail 等)の AI 用ドキュメント(Markdown)を返す。
    /// .md の物理的な置き場は Extras 本体プロジェクト(ScriptObjectDocs/)だが、WASM に配信しないため
    /// 埋め込み先はこの Designer 側アセンブリ(リンク EmbeddedResource)。
    /// リソース名 "….ScriptObjectDocs.&lt;型名&gt;.md" を Extras 本体アセンブリの同名 public 型へ対応付ける
    /// (型を1つずつ列挙しなくてよい)。登録は ExtrasDesignerInitializer が ScriptObjectCatalog.Add で行う。
    /// </summary>
    static class ExtrasScriptObjectDocs
    {
        static readonly Assembly DocAsm = typeof(ExtrasScriptObjectDocs).Assembly;

        public static Dictionary<Type, string> GetScriptObjectDocs()
        {
            var prefix = $"{DocAsm.GetName().Name}.ScriptObjectDocs.";
            const string suffix = ".md";

            // 型名 → Extras 本体アセンブリ内の public 型。
            var typesByName = typeof(ScriptObjects.Excel).Assembly.GetTypes()
                .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract)
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new Dictionary<Type, string>();
            foreach (var name in DocAsm.GetManifestResourceNames())
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal))
                    continue;

                var typeName = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length);
                if (!typesByName.TryGetValue(typeName, out var type)) continue;

                var md = Load(name);
                if (md != null) result[type] = md;
            }
            return result;
        }

        static string? Load(string resourceName)
        {
            using var stream = DocAsm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
