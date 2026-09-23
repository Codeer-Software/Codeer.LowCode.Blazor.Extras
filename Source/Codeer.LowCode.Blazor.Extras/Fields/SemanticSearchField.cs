using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// SemanticSearchField のランタイム。UI も読み込みデータも持たない。
    /// Submit のとき、対象フィールドのどれかが変更されていれば (新規なら常に) 行を文章にして送る。ベクトルはサーバー (SemanticSearchIndexer) が付ける。
    /// <para>
    /// スクリプトからは再索引 (溜まっている行全部の文章とベクトルを作り直す) を起動できる: <c>Search.Reindex()</c> / <c>Search.ReindexMissing()</c>。
    /// サーバーのジョブとして走り、進捗は <see cref="ReindexProcessed"/> / <see cref="ReindexTotal"/> に届き、終わると <see cref="SemanticSearchFieldDesign.OnReindexCompleted"/> が呼ばれる。
    /// 誰が起動できるかは、この API がフィールドの読取権限で入口を検査し、行の書き込みが実行ユーザーの権限で通ることで決まる (ボタンを置くページの UserReadCondition でも絞れる)。
    /// スクリプトから参照するには、このフィールドがそのレイアウトの DataOnlyFields (またはレイアウト) に入っていること。
    /// </para>
    /// </summary>
    public class SemanticSearchField(SemanticSearchFieldDesign design) : FieldBase<SemanticSearchFieldDesign>(design)
    {
        static readonly TimeSpan _fastInterval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _slowInterval = TimeSpan.FromMilliseconds(2500);
        static readonly TimeSpan _fastPeriod = TimeSpan.FromSeconds(10);
        static readonly TimeSpan _maxWait = TimeSpan.FromHours(2);

        /// <summary>
        /// 再索引 API のベース URL (POST {EndPoint} / GET・DELETE {EndPoint}/{requestId})。
        /// URL はアプリ (Controller を持つ側) の持ち物なので起動時に一度設定する (テンプレートは "/api/semantic_search/reindex")。
        /// </summary>
        [ScriptHide]
        public static string EndPoint { get; set; } = string.Empty;

        /// <summary>
        /// ホスト側フック。設定されていれば (デスクトップアプリ等) HTTP を使わず直接処理する。途中経過は IProgress に報告し、確定した状態を返す。
        /// </summary>
        [ScriptHide]
        public static Func<SemanticSearchField, SemanticSearchReindexRequest, IProgress<SemanticSearchReindexStatusResponse>, CancellationToken, Task<SemanticSearchReindexStatusResponse>>? ReindexCoreAsync { get; set; }

        CancellationTokenSource? _reindexCancellation;
        string _reindexRequestId = string.Empty;

        /// <summary>対象フィールドのどれかが変更されているか (= この Submit で文章を送るか)。自前の状態は持たず対象フィールドに追従する。</summary>
        [ScriptHide]
        public override bool IsModified
            => Design.HasColumns && SemanticSearchText.SourceFields(Module.Design, Design).Any(n => Module.GetField(n)?.IsModified == true);

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
        {
            if (!Design.HasColumns || (!Module.IsNewData && !IsModified)) return new();
            var text = SemanticSearchText.Build(Services.AppInfoService.GetDesignData(), Module.Design, Module.GetData(), Design);
            return new() { FieldData = new SemanticSearchFieldData { Text = text } };
        }

        /// <summary>今の行を索引用の文章にしたもの (確認用。Submit で送られるのと同じ規則)。</summary>
        public string Text
            => Design.HasColumns ? SemanticSearchText.Build(Services.AppInfoService.GetDesignData(), Module.Design, Module.GetData(), Design) : string.Empty;

        /// <summary>再索引が走っている間 true。</summary>
        public bool IsReindexing { get; private set; }

        /// <summary>再索引で書き直した行数 (走っている間は途中経過)。</summary>
        public int ReindexProcessed { get; private set; }

        /// <summary>再索引の対象行数 (分かるまでは 0)。</summary>
        public int ReindexTotal { get; private set; }

        /// <summary>最後の再索引のエラー (成功なら空)。</summary>
        public string ReindexError { get; private set; } = string.Empty;

        /// <summary>再索引を起動できる設定か (エンドポイントかホストフックのどちらかがある)。</summary>
        [ScriptHide]
        public bool IsReindexConfigured => ReindexCoreAsync != null || (Http != null && !string.IsNullOrEmpty(EndPoint));

        //ホスト (デザイナ等) には登録されていないことがあるため任意解決
        IHttpService? Http => Services.Provider?.GetService<IHttpService>();

        /// <summary>全行の文章とベクトルを作り直す (埋め込みモデルを変えたとき・フィールドを後から置いたとき)。走っている間は無視。</summary>
        [ScriptName("Reindex")]
        public Task ReindexAsync() => StartReindexAsync(missingOnly: false);

        /// <summary>ベクトルがまだ無い行だけ索引を付ける (埋め込みに失敗した行の穴埋め)。走っている間は無視。</summary>
        [ScriptName("ReindexMissing")]
        public Task ReindexMissingAsync() => StartReindexAsync(missingOnly: true);

        /// <summary>走っている再索引を中断する (サーバーにも伝える)。</summary>
        [ScriptName("CancelReindex")]
        public async Task CancelReindexAsync()
        {
            if (!IsReindexing) return;
            var http = Http;
            if (ReindexCoreAsync == null && http != null && !string.IsNullOrEmpty(_reindexRequestId))
            {
                using var _ = http.AddChecker((_, _) => { });
                await http.DeleteAsync($"{EndPoint}/{_reindexRequestId}", loading: false);
            }
            _reindexCancellation?.Cancel();
        }

        async Task StartReindexAsync(bool missingOnly)
        {
            if (IsReindexing || !IsReindexConfigured || !Design.HasColumns) return;
            var request = new SemanticSearchReindexRequest { ModuleName = Module?.Design.Name ?? string.Empty, FieldName = Design.Name, MissingOnly = missingOnly };
            IsReindexing = true;
            ReindexProcessed = 0;
            ReindexTotal = 0;
            ReindexError = string.Empty;
            _reindexRequestId = string.Empty;
            _reindexCancellation = new CancellationTokenSource();
            NotifyStateChanged();
            try
            {
                var status = await RunReindexAsync(request, _reindexCancellation.Token);
                Apply(status);
            }
            catch (OperationCanceledException)
            {
                Apply(new SemanticSearchReindexStatusResponse { Status = AIChatJobStatus.Canceled, Processed = ReindexProcessed, Total = ReindexTotal });
            }
            catch (Exception e)
            {
                Apply(new SemanticSearchReindexStatusResponse { Status = AIChatJobStatus.Error, Error = e.Message, Processed = ReindexProcessed, Total = ReindexTotal });
            }
            finally
            {
                IsReindexing = false;
                _reindexCancellation?.Dispose();
                _reindexCancellation = null;
                NotifyStateChanged();
            }
            await Module.ExecuteScriptAsync(Design.OnReindexCompleted);
        }

        async Task<SemanticSearchReindexStatusResponse> RunReindexAsync(SemanticSearchReindexRequest request, CancellationToken token)
        {
            if (ReindexCoreAsync != null)
            {
                var progress = new Progress<SemanticSearchReindexStatusResponse>(Apply);
                return await ReindexCoreAsync(this, request, progress, token);
            }

            var http = Http ?? throw new InvalidOperationException("IHttpService is not available.");
            //通信エラーはトーストに流さず、このフィールドのエラーにまとめる
            string? lastError = null;
            using var checker = http.AddChecker((_, message) => lastError = message);
            var sent = await http.PostAsJsonAsync<SemanticSearchReindexRequest, SemanticSearchReindexResponse>(EndPoint, request, loading: false);
            if (sent == null || string.IsNullOrEmpty(sent.RequestId))
                return new() { Status = AIChatJobStatus.Error, Error = lastError ?? Properties.Resources.SemanticSearchReindex_Failed };
            _reindexRequestId = sent.RequestId;

            var started = DateTime.Now;
            while (true)
            {
                var elapsed = DateTime.Now - started;
                await Task.Delay(elapsed < _fastPeriod ? _fastInterval : _slowInterval, token);

                var status = await http.GetFromJsonAsync<SemanticSearchReindexStatusResponse>($"{EndPoint}/{sent.RequestId}", loading: false);
                if (status == null)
                {
                    token.ThrowIfCancellationRequested();
                    return new() { Status = AIChatJobStatus.Error, Error = lastError ?? Properties.Resources.SemanticSearchReindex_Lost, Processed = ReindexProcessed, Total = ReindexTotal };
                }
                Apply(status);
                if (!status.IsRunning) return status;
                if (DateTime.Now - started > _maxWait)
                    return new() { Status = AIChatJobStatus.Error, Error = Properties.Resources.SemanticSearchReindex_Timeout, Processed = status.Processed, Total = status.Total };
            }
        }

        void Apply(SemanticSearchReindexStatusResponse status)
        {
            ReindexProcessed = status.Processed;
            ReindexTotal = status.Total;
            ReindexError = status.Status == AIChatJobStatus.Error ? (string.IsNullOrEmpty(status.Error) ? Properties.Resources.SemanticSearchReindex_Failed : status.Error)
                : status.Status == AIChatJobStatus.Canceled ? Properties.Resources.SemanticSearchReindex_Canceled
                : string.Empty;
            NotifyStateChanged();
        }
    }
}
