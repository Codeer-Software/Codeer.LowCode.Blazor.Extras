using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.AIChat;
using Codeer.LowCode.Blazor.Extras.Api;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// SemanticSearchField のランタイム。UI も読み込みデータも持たず、Submit でも何も送らない
    /// (索引 = 文章とベクトルは、保存のたびにサーバーの SemanticSearchService が保存した行から作る)。
    /// <para>
    /// スクリプトからは再索引 (溜まっている行全部の文章とベクトルを作り直す) を起動できる: <c>Search.Reindex()</c> / <c>Search.ReindexMissing()</c>。
    /// サーバーのジョブとして走り、進捗は <see cref="ReindexProcessed"/> / <see cref="ReindexTotal"/> に届き、終わると <see cref="SemanticSearchFieldDesign.OnReindexCompleted"/> が呼ばれる。
    /// 誰が起動できるかは、この API がフィールドの書き込み権限 (ユーザー権限だけ) で入口を検査することで決まる (索引自体は権限を通さずサーバーが作る)。
    /// スクリプトから参照するには、このフィールドがそのレイアウトの DataOnlyFields (またはレイアウト) に入っていること。
    /// </para>
    /// </summary>
    public class SemanticSearchField(SemanticSearchFieldDesign design) : FieldBase<SemanticSearchFieldDesign>(design), ISearchableField
    {
        /// <summary>検索レイアウトの検索欄に入れた文章 (探したい内容)。空なら条件なし。</summary>
        public string? SearchText { get; private set; }

        /// <summary>検索欄の文章を設定する。</summary>
        [ScriptMethodToProperty(nameof(SearchText))]
        public async Task SetSearchTextAsync(string? text)
        {
            if (SearchText == text) return;
            SearchText = text;
            ClearError();
            NotifyStateChanged();
            await Task.CompletedTask;
        }

        /// <summary>検索レイアウトのキーワード欄に入れた語 (空白区切り・すべて含む行に絞る)。空なら条件なし。</summary>
        public string? KeywordText { get; private set; }

        /// <summary>キーワード欄の語を設定する。</summary>
        [ScriptMethodToProperty(nameof(KeywordText))]
        public async Task SetKeywordTextAsync(string? text)
        {
            if (KeywordText == text) return;
            KeywordText = text;
            ClearError();
            NotifyStateChanged();
            await Task.CompletedTask;
        }

        /// <summary>
        /// 検索欄の文章とキーワードを 1 つの意味検索の条件にする (文章は文章だけ。埋め込みはサーバーの前処理が作る)。近い順に並べたいときは、一覧の並びに "フィールド名.Value" を入れておく
        /// (文章が空のときは本体がその並びを飛ばす)。キーワードは文章の列が語をすべて含む行に絞る (埋め込み不要)。
        /// キーワード欄を出していなくても (<see cref="SemanticSearchFieldDesign.KeywordSearch"/> = false) スクリプトで設定すれば条件に入る。
        /// </summary>
        [ScriptHide]
        public MatchConditionBase? GetMatchCondition()
        {
            var text = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
            var keywords = SemanticSearchKeywords.Split(KeywordText);
            if (text == null && keywords.Count == 0) return null;
            return new FieldMatchCondition
            {
                FieldName = Design.Name,
                Children = [new SemanticMatchCondition
                {
                    FieldName = Design.Name,
                    Text = text ?? string.Empty,
                    MaxDistance = text == null ? null : Design.SearchMaxDistance,
                    Keywords = string.Join(' ', keywords),
                }],
            };
        }

        /// <summary>URL 等から戻した条件を検索欄に反映する。</summary>
        [ScriptHide]
        public async Task SetMatchConditionAsync(FieldMatchCondition condition)
        {
            if (condition.Children.FirstOrDefault() is not SemanticMatchCondition semantic) return;
            await SetSearchTextAsync(string.IsNullOrWhiteSpace(semantic.Text) ? null : semantic.Text);
            await SetKeywordTextAsync(string.IsNullOrWhiteSpace(semantic.Keywords) ? null : semantic.Keywords);
        }

        [ScriptHide]
        public async Task ClearMatchConditionAsync()
        {
            await SetSearchTextAsync(null);
            await SetKeywordTextAsync(null);
        }

        static readonly TimeSpan _fastInterval = TimeSpan.FromSeconds(1);
        static readonly TimeSpan _slowInterval = TimeSpan.FromMilliseconds(2500);
        static readonly TimeSpan _fastPeriod = TimeSpan.FromSeconds(10);
        static readonly TimeSpan _maxWait = TimeSpan.FromHours(2);

        /// <summary>
        /// 再索引専用の API のベース URL (POST {EndPoint} / GET・DELETE {EndPoint}/{requestId})。
        /// 空 (既定) なら共通の口 (<see cref="ExtrasTransport"/> = ホストの 1 本の API の SemanticSearchApi) を使う。再索引専用の Controller を持つホストが起動時に設定する。
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

        /// <summary>値を持たないので変更もない (索引はサーバーが保存した行から作る)。</summary>
        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data)
            => await Task.CompletedTask;

        /// <summary>何も送らない (索引はサーバーが保存した行から作る)。</summary>
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        /// <summary>再索引が走っている間 true。</summary>
        public bool IsReindexing { get; private set; }

        /// <summary>再索引で書き直した行数 (走っている間は途中経過)。</summary>
        public int ReindexProcessed { get; private set; }

        /// <summary>再索引の対象行数 (分かるまでは 0)。</summary>
        public int ReindexTotal { get; private set; }

        /// <summary>最後の再索引のエラー (成功なら空)。</summary>
        public string ReindexError { get; private set; } = string.Empty;

        /// <summary>再索引を起動できる設定か (ホストフック・専用のエンドポイント・共通の口のどれかがある)。</summary>
        internal bool IsReindexConfigured => ReindexCoreAsync != null || (!string.IsNullOrEmpty(EndPoint) ? Http != null : ExtrasTransport.CanSend(Http));

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
            if (ReindexCoreAsync == null && !string.IsNullOrEmpty(_reindexRequestId))
            {
                //専用のルート (EndPoint) があればそこへ DELETE、無ければ共通の口 (ExtrasTransport) の Cancel。失敗は通知しない
                if (!string.IsNullOrEmpty(EndPoint))
                {
                    if (http != null)
                    {
                        using var _ = http.AddChecker((_, _) => { });
                        await http.DeleteAsync($"{EndPoint}/{_reindexRequestId}", loading: false);
                    }
                }
                else
                {
                    await ExtrasTransport.CallAsync<object>(http, SemanticSearchApiNames.Feature, SemanticSearchApiNames.Cancel, new SemanticSearchJobRequest { RequestId = _reindexRequestId }, loading: false, onError: (_, _) => { });
                }
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
            await Module!.ExecuteScriptAsync(Design.OnReindexCompleted);
        }

        async Task<SemanticSearchReindexStatusResponse> RunReindexAsync(SemanticSearchReindexRequest request, CancellationToken token)
        {
            if (ReindexCoreAsync != null)
            {
                var progress = new Progress<SemanticSearchReindexStatusResponse>(Apply);
                return await ReindexCoreAsync(this, request, progress, token);
            }

            //専用のルート (EndPoint) があればそこへ POST / GET、無ければ共通の口 (ExtrasTransport) の Reindex / Status。
            //通信エラーはトーストに流さず、このフィールドのエラーにまとめる
            var http = Http;
            var ownRoute = !string.IsNullOrEmpty(EndPoint);
            if (ownRoute && http == null) throw new InvalidOperationException("IHttpService is not available.");
            string? lastError = null;
            using var checker = ownRoute ? http!.AddChecker((_, message) => lastError = message) : null;
            async Task<T?> CallAsync<T>(Func<IHttpService, Task<T?>> byOwnRoute, string action, object apiRequest) where T : class
                => ownRoute
                    ? await byOwnRoute(http!)
                    : (await ExtrasTransport.CallAsync<T>(http, SemanticSearchApiNames.Feature, action, apiRequest, loading: false, onError: (_, message) => lastError = message)).Response;

            var sent = await CallAsync(h => h.PostAsJsonAsync<SemanticSearchReindexRequest, SemanticSearchReindexResponse>(EndPoint, request, loading: false), SemanticSearchApiNames.Reindex, request);
            if (sent == null || string.IsNullOrEmpty(sent.RequestId))
                return new() { Status = AIChatJobStatus.Error, Error = lastError ?? Properties.Resources.SemanticSearchReindex_Failed };
            _reindexRequestId = sent.RequestId;
            var statusRequest = new SemanticSearchJobRequest { RequestId = sent.RequestId };

            var started = DateTime.Now;
            while (true)
            {
                var elapsed = DateTime.Now - started;
                await Task.Delay(elapsed < _fastPeriod ? _fastInterval : _slowInterval, token);

                var status = await CallAsync(h => h.GetFromJsonAsync<SemanticSearchReindexStatusResponse>($"{EndPoint}/{sent.RequestId}", loading: false), SemanticSearchApiNames.Status, statusRequest);
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
