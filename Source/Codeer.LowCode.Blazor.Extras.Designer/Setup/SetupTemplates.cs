using System.IO;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// セットアップ用モジュールテンプレートの読み込み。
    /// 実体は Example のデザイン (実機確認済み) をリンク埋め込みしたもの。csproj の SetupTemplates 埋め込みと連動。
    /// </summary>
    internal static class SetupTemplates
    {
        const string Prefix = "Codeer.LowCode.Blazor.Extras.Designer.SetupTemplates.";

        internal static string Load(string fileName)
        {
            var assembly = typeof(SetupTemplates).Assembly;
            using var stream = assembly.GetManifestResourceStream(Prefix + fileName)
                ?? throw new InvalidOperationException($"Template not found: {fileName}");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
