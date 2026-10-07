using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.Utils;
using CoreServices = Codeer.LowCode.Blazor.RequestInterfaces.Services;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>
    /// TagField の候補 (フィールドごとに 1 つ)。候補はサーバーで絞る: タグ付けモジュールを
    /// 「タグ名でグループ化・件数・件数の多い順 → 名前順・上位 10 件・打った文字で部分一致」で 1 回引く (本体の集計)。
    /// 効くのはタグ付けモジュールの読み取り条件 (UserRead / DataRead)。タグを付けるモジュール (親) の行の条件は効かない。
    /// </summary>
    internal sealed class TagCandidateProvider
    {
        /// <summary>1 回に引く数 (= 画面に出す最大の数)。設定にはしない。</summary>
        internal const int Limit = 10;

        readonly Func<CoreServices> _services;
        readonly Func<TagBinding?> _binding;

        internal TagCandidateProvider(Func<CoreServices> services, Func<TagBinding?> binding)
        {
            _services = services;
            _binding = binding;
        }

        /// <summary>打った文字を含むタグ (よく使われている順)。何も打っていなければ問い合わせない。</summary>
        internal async Task<List<string>> SuggestAsync(string text)
        {
            text = text.Trim();
            var services = _services();
            var binding = _binding();
            if (text.Length == 0 || binding == null || services.AppInfoService.IsDesignMode) return new();
            var module = services.AppInfoService.GetDesignData().Modules.Find(binding.LinkModule);
            if (module == null || !module.HasUserReadPermission(services)) return new();

            var variable = $"{binding.TagNameField}.Value";
            var aggregate = new AggregateCondition(binding.LinkModule)
            {
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition { SearchTargetVariable = variable, Comparison = MatchComparison.Like, Value = new StringValue { Value = text } }),
                Groups = { new ValueGroup { Variable = variable } },
                Measures = { new AggregateMeasure { Function = AggregateFunction.Count, Name = "count" } },
                SortConditions =
                {
                    new AggregateSort { Target = AggregateSortTarget.Measure, Index = 0, IsDescending = true },
                    new AggregateSort { Target = AggregateSortTarget.Group, Index = 0 },
                },
                LimitCount = Limit,
            };
            try
            {
                var result = (await services.ModuleDataService.AggregateAsync(new List<AggregateCondition> { aggregate })).FirstOrDefault();
                return (result?.Rows ?? new())
                    .Select(e => e.KeyText(0) ?? string.Empty)
                    .Where(e => e.Length > 0)
                    .ToList();
            }
            catch (Exception e)
            {
                await services.Logger.Error(e.Message);
                return new();
            }
        }
    }
}
