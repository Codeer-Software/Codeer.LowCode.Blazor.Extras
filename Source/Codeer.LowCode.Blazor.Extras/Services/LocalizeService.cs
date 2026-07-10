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
            var rows = reader.ReadToEnd()
                .Split('\n')
                .Select(line => line.TrimEnd('\r'))
                .Where(line => line.Length > 0)
                .Select(line => line.Split('\t'))
                .ToList();
            if (rows.Count < 2) return new();

            var index = FindCultureIndex(rows[0]);

            Dictionary<string, string> dic = new();
            foreach (var row in rows.Skip(1))
            {
                if (row.Length <= index) continue;
                var key = row[0].Trim();
                if (key.Length == 0) continue;
                dic[key] = row[index].Trim();
            }
            return dic;
        }

        // ヘッダ行から現在のカルチャ名(例: ja-JP)の列を探す。大小文字は区別しない。
        // 見つからなければキーの右隣(列1)にフォールバックする。
        static int FindCultureIndex(string[] header)
        {
            var index = Array.FindIndex(
                header,
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
