using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Codeer.LowCode.Blazor.Extras.Server.AuditLog
{
    /// <summary>
    /// 監査ログを保存する部品。設定と出力先を持ち、何を記録するかは決めない (呼び出し側が <see cref="AuditEvent"/> を組み立てて渡す)。
    /// - 分類の絞り込み (<see cref="AuditLogSettings.Categories"/>。失敗・拒否は常に記録。試行は <see cref="AuditLogSettings.AttemptCategories"/>)
    /// - 全出力先に書く。書けない出力先があれば Strict なら <see cref="AuditLogException"/>、BestEffort なら ILogger の Critical
    /// - 保持期限切れの削除 (<see cref="PurgeAsync"/>。消した事実も System として記録)
    /// - デザインの版 (<see cref="AuditEvent.DesignVersion"/>)。ホストが版を渡せば全レコードに入れ、版の切替を System として記録する
    /// アプリで 1 つ (シングルトン)。リクエストの外 (バックグラウンドのジョブ・起動/停止) からもそのまま呼べる。
    /// </summary>
    public class AuditLogger
    {
        readonly AuditLogSettings _settings;
        readonly IReadOnlyList<IAuditSink> _sinks;
        readonly ILogger? _logger;
        readonly Func<HttpContext?, string>? _designVersion;
        readonly SemaphoreSlim _designLock = new(1, 1);
        string? _loadedDesignVersion;

        /// <param name="designVersion">
        /// デザインの版 (App.zip の SHA-256)。ホストが渡す。HttpContext があればそのリクエストが使う版 (リクエストの間は変わらない)、
        /// null ならこのプロセスが今読み込んでいる版を返す。渡さなければ版は記録しない。
        /// </param>
        public AuditLogger(AuditLogSettings settings, IEnumerable<IAuditSink> sinks, ILogger? logger = null, Func<HttpContext?, string>? designVersion = null)
        {
            _settings = settings;
            _sinks = sinks.ToList();
            _logger = logger;
            _designVersion = designVersion;
            //有効なのに出力先が無い設定は起動で止める (記録が静かに欠けるのを防ぐ)
            if (_settings.Enabled && _sinks.Count == 0)
                throw new InvalidOperationException("AuditLog is enabled but no sink is configured (AuditLogDatabase.DataSourceName / AuditLogFile.Directory).");
        }

        public bool IsEnabled => _settings.Enabled;

        public AuditLogSettings Settings => _settings;

        /// <summary>
        /// 記録の対象か。無効なら false。成功した操作は分類の絞り込み (Categories) に従い、失敗・拒否は常に true。
        /// 試行 (Attempt) は、その分類を記録する設定で、かつ AttemptCategories にあるとき。
        /// </summary>
        public bool ShouldRecord(AuditEvent e)
        {
            if (!IsEnabled) return false;
            return e.Result switch
            {
                AuditResult.Attempt => Accepts(e.Category) && _settings.EffectiveAttemptCategories.Contains(e.Category),
                AuditResult.Success => Accepts(e.Category),
                _ => true,
            };
        }

        bool Accepts(AuditCategory category)
            => _settings.Categories.Length == 0 || _settings.Categories.Contains(category);

        /// <summary>デザインの版。HttpContext を渡せばそのリクエストが使う版、null ならこのプロセスが今読み込んでいる版。ホストが版を渡していなければ空。</summary>
        public string GetDesignVersion(HttpContext? context)
            => _designVersion?.Invoke(context) ?? string.Empty;

        public async Task WriteAsync(AuditEvent e)
        {
            if (!IsEnabled) return;
            await RecordDesignLoadedAsync();
            //リクエストの外の記録 (起動・停止・掃除・バックグラウンドのジョブ) は、今読み込んでいる版
            if (string.IsNullOrEmpty(e.DesignVersion)) e.DesignVersion = GetDesignVersion(null);
            if (!ShouldRecord(e)) return;
            await WriteToSinksAsync(e);
        }

        //このプロセスが読み込んでいるデザインの版が替わったら System の Design.Loaded として記録する (インスタンスごとの切替の時刻)。
        //各行は自分で版を持つので紐づけには使わない。最初の 1 回は起動の記録 (Application.Start) が版を持つので書かない
        async Task RecordDesignLoadedAsync()
        {
            if (_designVersion == null || GetDesignVersion(null) == _loadedDesignVersion) return;
            await _designLock.WaitAsync();
            try
            {
                //切替の前に版を読んだ呼び出しが、切替の後に来て古い版へ戻す記録を書かないよう、ロックの中で読み直す
                var current = GetDesignVersion(null);
                if (current == _loadedDesignVersion) return;
                if (_loadedDesignVersion != null)
                {
                    var loaded = new AuditEvent { Category = AuditCategory.System, Action = "Design.Loaded", DesignVersion = current, Detail = $"Previous={_loadedDesignVersion}" };
                    if (ShouldRecord(loaded)) await WriteToSinksAsync(loaded);
                }
                _loadedDesignVersion = current;
            }
            finally
            {
                _designLock.Release();
            }
        }

        async Task WriteToSinksAsync(AuditEvent e)
        {
            List<Exception>? failures = null;
            foreach (var sink in _sinks)
            {
                try
                {
                    await sink.WriteAsync(e);
                }
                catch (Exception ex)
                {
                    //書けなかったレコードは内容ごとアプリのログに残す (ファイル出力と同じ JSON 1 行。後段が書けなかった操作や BestEffort ではこれが唯一の記録になる)
                    _logger?.LogCritical(ex, "Audit log write failed ({Sink}): {Event}", sink.GetType().Name, AuditEventJson.Serialize(e));
                    (failures ??= new()).Add(ex);
                }
            }
            if (failures != null && _settings.FailureMode == AuditFailureMode.Strict)
                throw new AuditLogException("Audit log could not be written.", failures.Count == 1 ? failures[0] : new AggregateException(failures));
        }

        /// <summary>保持期限 (RetentionDays) より古いレコードを全出力先から消し、消した数を System として記録する。RetentionDays が 0 なら何もしない。</summary>
        public async Task PurgeAsync()
        {
            if (!IsEnabled || _settings.RetentionDays <= 0) return;
            var cutoff = DateTime.UtcNow.AddDays(-_settings.RetentionDays);
            var counts = new List<string>();
            foreach (var sink in _sinks)
                counts.Add($"{sink.GetType().Name}={await sink.PurgeAsync(cutoff)}");
            await WriteAsync(new AuditEvent
            {
                Category = AuditCategory.System,
                Action = "AuditLog.Purge",
                Detail = $"olderThan={cutoff:O}; {string.Join("; ", counts)}",
            });
        }
    }

    /// <summary>Strict のとき、監査ログを書けなかった操作に投げる。</summary>
    public class AuditLogException : Exception
    {
        public AuditLogException(string message, Exception inner) : base(message, inner) { }
    }
}
