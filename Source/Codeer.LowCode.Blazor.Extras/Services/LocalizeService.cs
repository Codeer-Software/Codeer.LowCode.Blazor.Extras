using Codeer.LowCode.Blazor.Extras.Csv;
using Codeer.LowCode.Blazor.RequestInterfaces;
using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    public class LocalizeService
    {
        Dictionary<string, string> _dic = new();

        public string Localize(string text)
            => _dic.TryGetValue(text, out var localizedText) ? localizedText : text;

        public static LocalizeService? Create(string localizeResourceName, MemoryStream? mem)
        {
            var service = new LocalizeService();
            var lowName = localizeResourceName.ToLower();
            if (lowName.EndsWith(".tsv")) service._dic = FromTsv(mem);
            if (!service._dic.Any()) return null;
            return service;
        }

        static Dictionary<string, string> FromTsv(MemoryStream? mem)
        {
            if (mem == null) return new();

            mem.Position = 0;
            using var reader = new StreamReader(mem, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            //引用符付きセル (タブ・改行入りの訳文) も扱えるよう CSV パーサを共用する
            var rows = CsvTextParser.Parse(reader, '\t');
            if (rows.Count < 2) return new();

            var index = FindCultureIndex(rows[0]);

            Dictionary<string, string> dic = new();
            foreach (var row in rows.Skip(1))
            {
                if (row.Count <= index) continue;
                var key = row[0].Trim();
                if (key.Length == 0) continue;
                dic[key] = row[index].Trim();
            }
            return dic;
        }

        // ヘッダ行から現在のカルチャ名(例: ja-JP)の列を探す。大小文字は区別しない。
        // 見つからなければキーの右隣(列1)にフォールバックする。
        static int FindCultureIndex(List<string> header)
        {
            var index = header.FindIndex(
                h => h.Trim().Equals(CultureInfo.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
            return index < 1 ? 1 : index;
        }
    }

    public static class LocalizeServiceHelper
    {
        public static async Task<LocalizeService?> CreateLocalizeService(this IAppInfoService app)
        {
            var _design = app.GetDesignData();
            if (string.IsNullOrEmpty(_design.AppSettings.LocalizeResourcePath)) return null;
            return LocalizeService.Create(_design.AppSettings.LocalizeResourcePath, await app.GetResourceAsync(_design.AppSettings.LocalizeResourcePath));
        }
    }
}
