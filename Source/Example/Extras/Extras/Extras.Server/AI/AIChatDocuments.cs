using Codeer.LowCode.Blazor.Extras.Server.AI.Chat;
using Extras.Server.Services;
using System.IO.Compression;

namespace Extras.Server.AI
{
    /// <summary>
    /// AI に渡す補足文書の読み取り (アプリの持ち物)。デザインプロジェクトの <c>Resources/{フォルダ}/*.md</c> を App.zip から読む。
    /// フォルダは AIChatField のデザインの DocumentFolder で、チャットごとに違う文書の組を渡せる。
    /// 業務用語の定義や集計の決まりをデザイン担当者 (や CCFD の Claude) がモジュール定義と一緒に書き、デプロイで反映される。
    /// App.zip の更新時刻が変わったら読み直す (ホットリロードと同じ粒度)。
    /// </summary>
    internal static class AIChatDocuments
    {
        static readonly object _sync = new();
        static DateTime _loadedFor;
        static readonly Dictionary<string, IReadOnlyList<AIChatDocument>> _cache = new(StringComparer.OrdinalIgnoreCase);

        /// <param name="folder">Resources からの相対フォルダ (AIChatField の DocumentFolder)。空なら文書なし</param>
        public static IReadOnlyList<AIChatDocument> Read(string folder)
        {
            folder = (folder ?? string.Empty).Replace('\\', '/').Trim('/');
            if (folder.Length == 0) return Array.Empty<AIChatDocument>();
            var zipPath = Path.Combine(SystemConfig.Instance.DesignFileDirectory, "App.zip");   //サーバーが配信しているデザイン (DesignDataFileManager と同じ場所)
            if (!File.Exists(zipPath)) return Array.Empty<AIChatDocument>();
            var stamp = File.GetLastWriteTimeUtc(zipPath);
            lock (_sync)
            {
                if (stamp != _loadedFor) { _cache.Clear(); _loadedFor = stamp; }
                if (_cache.TryGetValue(folder, out var cached)) return cached;
                var prefix = "Resources/" + folder + "/";
                var list = new List<AIChatDocument>();
                using var zip = ZipFile.OpenRead(zipPath);
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                    using var reader = new StreamReader(entry.Open());
                    list.Add(new AIChatDocument(Path.GetFileNameWithoutExtension(name), reader.ReadToEnd()));
                }
                var result = list.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
                _cache[folder] = result;
                return result;
            }
        }
    }
}
