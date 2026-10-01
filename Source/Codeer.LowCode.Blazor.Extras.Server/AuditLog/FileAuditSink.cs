using System.Globalization;
using System.Text;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// JSON Lines のファイルへ書く出力先。<c>audit-{ホスト名}-{yyyyMMdd}.jsonl</c> に 1 行 1 レコードで追記する
    /// (ホスト名で分けるので複数インスタンスが同じフォルダを共有できる)。SIEM やログ収集への転送元として使う。
    /// 同じホストの別プロセス (IIS のオーバーラップリサイクル中の新旧ワーカー) と同時に書いても共有違反にならないよう、
    /// 他のプロセスの読み書きを許して開く (追記モードなので互いの行を壊さない)。
    /// </summary>
    public class FileAuditSink : IAuditSink
    {
        readonly string _directory;
        readonly SemaphoreSlim _lock = new(1, 1);

        public FileAuditSink(AuditLogFileSettings settings) : this(settings.Directory) { }

        public FileAuditSink(string directory) => _directory = directory;

        public async Task WriteAsync(AuditEvent e)
        {
            var line = Encoding.UTF8.GetBytes(AuditEventJson.Serialize(e) + "\n");
            await _lock.WaitAsync();
            try
            {
                Directory.CreateDirectory(_directory);
                await using var stream = new FileStream(Path.Combine(_directory, FileName(e.Host, e.OccurredAtUtc)),
                    FileMode.Append, FileAccess.Write, FileShare.ReadWrite, bufferSize: 4096, useAsync: true);
                await stream.WriteAsync(line);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>ファイル名の日付が期限より前のファイルを消す (書きかけの当日分は残る)。</summary>
        public async Task<int> PurgeAsync(DateTime olderThanUtc)
        {
            if (!Directory.Exists(_directory)) return 0;
            var count = 0;
            await _lock.WaitAsync();
            try
            {
                foreach (var path in Directory.GetFiles(_directory, "audit-*.jsonl"))
                {
                    var date = DateOf(Path.GetFileNameWithoutExtension(path));
                    if (date == null || date.Value >= olderThanUtc.Date) continue;
                    File.Delete(path);
                    count++;
                }
            }
            finally
            {
                _lock.Release();
            }
            return count;
        }

        static string FileName(string host, DateTime utc)
            => $"audit-{Sanitize(host)}-{utc:yyyyMMdd}.jsonl";

        static string Sanitize(string host)
            => string.Concat(host.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '-' ? '_' : c));

        static DateTime? DateOf(string fileNameWithoutExtension)
        {
            var i = fileNameWithoutExtension.LastIndexOf('-');
            if (i < 0) return null;
            return DateTime.TryParseExact(fileNameWithoutExtension[(i + 1)..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
        }
    }
}
