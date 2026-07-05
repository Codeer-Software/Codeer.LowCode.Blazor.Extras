using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras
{
    /// <summary>
    /// Extras 系フィールドの AI 用ドキュメント(Markdown)を返す。
    /// このライブラリはデザイナ(Codeer.LowCode.Blazor.Designer)を参照しないため、ここでは登録せず
    /// フィールドデザイン型 → Markdown の対応だけを提供する。
    /// 実際のデザイナへの登録は Designer 側(ExtrasDesignerInitializer)が FieldCatalog.Add で行う。
    /// Markdown は本アセンブリの埋め込みリソース(FieldDocs/&lt;型名&gt;.md)から読み込み、同名の
    /// フィールドデザイン型へ対応付ける(型を1つずつ列挙しなくてよい)。
    /// </summary>
    public static class ExtrasFieldDocs
    {
        static readonly Assembly Asm = typeof(ExtrasFieldDocs).Assembly;

        public static Dictionary<Type, string> GetFieldDocs()
        {
            var prefix = $"{Asm.GetName().Name}.FieldDocs.";
            const string suffix = ".md";

            // 型名 → フィールドデザイン型(本アセンブリ内)。
            var typesByName = Asm.GetTypes()
                .Where(t => t.IsPublic && t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(FieldDesignBase)))
                .GroupBy(t => t.Name)
                .ToDictionary(g => g.Key, g => g.First());

            var result = new Dictionary<Type, string>();
            foreach (var name in Asm.GetManifestResourceNames())
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
            using var stream = Asm.GetManifestResourceStream(resourceName);
            if (stream == null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
