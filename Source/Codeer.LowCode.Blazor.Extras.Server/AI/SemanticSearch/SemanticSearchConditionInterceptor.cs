using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.SemanticSearch;
using Codeer.LowCode.Blazor.Extras.Server.Properties;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;

namespace Codeer.LowCode.Blazor.Extras.Server.AI.SemanticSearch
{
    /// <summary>
    /// 一覧検索・集計の条件の前処理: 文章だけの意味検索の条件 (<see cref="SemanticMatchCondition.Text"/> あり・Vector 空。画面の検索欄が作る) に、
    /// 索引付けと同じ埋め込みプロバイダでベクトルを入れる。検索の中身 (SQL・権限) は本体と SemanticSearchFieldDesign のまま変えない。
    /// 保存と読み出しの後処理には関わらない (素通し)。
    /// </summary>
    internal sealed class SemanticSearchConditionInterceptor(SemanticSearchService service) : IModuleDataIOInterceptor
    {
        public Task<List<ModuleSubmitResult>> SubmitAsync(ModuleDataIOInternalAccess io, List<ModuleSubmitData> transactionData, Func<Task<List<ModuleSubmitResult>>> next)
            => next();

        public Task GetListAsync(ModuleDataIOInternalAccess io, SearchCondition condition, Paging<ModuleData> result)
            => Task.CompletedTask;

        public async Task PrepareConditionAsync(ModuleDataIOInternalAccess io, ModuleMatchCondition condition)
        {
            var targets = Collect(condition.Condition).Where(e => e.Vector.Length == 0 && !string.IsNullOrWhiteSpace(e.Text)).ToList();
            if (targets.Count == 0) return;

            var provider = service.GetProvider() ?? throw LowCodeException.Create(Resources.SemanticSearch_ProviderNotConfigured);
            var texts = targets.Select(e => e.Text.Trim()).Distinct().ToList();
            var vectors = await provider.EmbedAsync(texts);
            var byText = texts.Select((text, i) => (text, vector: vectors[i])).ToDictionary(e => e.text, e => e.vector);
            foreach (var target in targets) target.Vector = byText[target.Text.Trim()];
        }

        static IEnumerable<SemanticMatchCondition> Collect(MatchConditionBase? condition)
        {
            switch (condition)
            {
                case SemanticMatchCondition semantic:
                    yield return semantic;
                    break;
                case MultiMatchCondition multi:
                    foreach (var child in multi.Children)
                        foreach (var e in Collect(child)) yield return e;
                    break;
            }
        }
    }
}
