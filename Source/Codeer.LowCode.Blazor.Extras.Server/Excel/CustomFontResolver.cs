using PdfSharp.Fonts;

namespace Codeer.LowCode.Blazor.Extras.Server.Excel
{
    public class CustomFontResolver : IFontResolver
    {
        readonly string _fontFileDirectory;

        public CustomFontResolver(string fontFileDirectory) => _fontFileDirectory = fontFileDirectory;

        public byte[] GetFont(string faceName)
        {
            var path = Path.Combine(_fontFileDirectory, faceName + ".ttf");
            if (!File.Exists(path)) path = Path.Combine(_fontFileDirectory, "NotoSansJP.ttf");
            return File.ReadAllBytes(path);
        }

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
        {
            familyName = familyName.Replace(" ", "");
            if (isBold) familyName += "#b";
            return new FontResolverInfo(familyName.Replace(" ", ""));
        }
    }
}
