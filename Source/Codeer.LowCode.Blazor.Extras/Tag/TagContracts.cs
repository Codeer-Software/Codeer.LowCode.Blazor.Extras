using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>TagField の結び付き。タグ付けモジュール (TagLinkContractField) の役割を、実際のフィールド名に解決したもの。</summary>
    internal sealed class TagBinding
    {
        /// <summary>タグ付けモジュール。</summary>
        public string LinkModule { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグを付けたレコードの Id のフィールド。</summary>
        public string OwnerIdField { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグ名のフィールド。</summary>
        public string TagNameField { get; init; } = string.Empty;
        /// <summary>タグを付けるレコードの側の、OwnerId に入る値の変数 (通常 "Id.Value")。</summary>
        public string OwnerKeyVariable { get; init; } = string.Empty;
    }

    internal static class TagContracts
    {
        /// <summary>TagField の設定から結び付きを解決する。欠けていれば null (デザインチェックが指摘する不備)。</summary>
        internal static TagBinding? Resolve(DesignData design, TagFieldDesign field)
        {
            var linkModuleName = field.SearchCondition?.ModuleName ?? string.Empty;
            var link = LinkContract(design.Modules.Find(linkModuleName));
            if (link == null) return null;
            var ownerKey = OwnerKeyVariable(field.SearchCondition!, link.OwnerId);
            if (string.IsNullOrEmpty(ownerKey) || string.IsNullOrEmpty(link.TagName)) return null;
            return new TagBinding
            {
                LinkModule = linkModuleName,
                OwnerIdField = link.OwnerId,
                TagNameField = link.TagName,
                OwnerKeyVariable = ownerKey,
            };
        }

        internal static TagLinkContractFieldDesign? LinkContract(ModuleDesign? module)
            => module?.Fields.OfType<TagLinkContractFieldDesign>().FirstOrDefault();

        /// <summary>検索条件の中の「OwnerId.Value = (本体の変数)」から本体の変数を取り出す (無ければ空)。</summary>
        internal static string OwnerKeyVariable(SearchCondition condition, string ownerIdField)
            => Flatten(condition.Condition)
                .OfType<FieldVariableMatchCondition>()
                .FirstOrDefault(e => e.Comparison == MatchComparison.Equal && e.SearchTargetVariable == $"{ownerIdField}.Value")
                ?.Variable ?? string.Empty;

        static IEnumerable<MatchConditionBase> Flatten(MatchConditionBase? condition)
        {
            if (condition == null) yield break;
            yield return condition;
            var children = condition is MultiMatchCondition multi ? multi.Children : null;
            if (children == null) yield break;
            foreach (var child in children)
                foreach (var e in Flatten(child)) yield return e;
        }

        /// <summary>レコードたちのタグ付け行をまとめて引く条件 (一覧の行・再索引)。並びは TagField の検索条件の並び。</summary>
        internal static SearchCondition LinkRowsCondition(TagBinding binding, TagFieldDesign field, IEnumerable<string> ownerIds)
            => new()
            {
                ModuleName = binding.LinkModule,
                Condition = MultiMatchCondition.And(new FieldValueMatchCondition
                {
                    SearchTargetVariable = $"{binding.OwnerIdField}.Value",
                    Comparison = MatchComparison.In,
                    Value = MultiTypeValue.Create(ownerIds.Distinct().ToList()),
                }),
                SortConditions = field.SearchCondition?.SortConditions?.ToList() ?? new(),
                SelectFields = new List<string> { SystemFieldNames.Id, binding.OwnerIdField, binding.TagNameField },
            };

        /// <summary>条件に合う行を全部読む (LimitCount をページの大きさにして、最後のページまで)。クライアントとサーバーで読み方 (read) だけが違う。</summary>
        internal static async Task<List<ModuleData>> ReadAllAsync(Func<SearchCondition, int, Task<Paging<ModuleData>?>> read, SearchCondition condition, int pageSize = 1000)
        {
            condition.LimitCount = pageSize;
            var rows = new List<ModuleData>();
            for (var pageIndex = 0; ; pageIndex++)
            {
                var items = (await read(condition, pageIndex))?.Items ?? new();
                rows.AddRange(items);
                if (items.Count < pageSize) return rows;
            }
        }

        /// <summary>
        /// 行たち (同じモジュール) に、TagField のタグ付け行を入れる (一覧の読み込みは子の一覧を含まないため。サーバーの再索引で使う)。
        /// fieldNames に無い TagField は触らない。
        /// 本体側の口 B (一覧のページの行の子の一覧をまとめて読む) ができたら、そちらに置き換える。
        /// </summary>
        internal static async Task FillTagRowsAsync(DesignData design, ModuleDesign module, IReadOnlyList<ModuleData> rows, IEnumerable<string> fieldNames,
            Func<SearchCondition, int, Task<Paging<ModuleData>?>> read)
        {
            var names = fieldNames.ToHashSet();
            foreach (var field in module.Fields.OfType<TagFieldDesign>().Where(e => names.Contains(e.Name)))
            {
                var binding = Resolve(design, field);
                if (binding == null) continue;
                var keyField = FieldOfVariable(binding.OwnerKeyVariable);
                string KeyOf(ModuleData row) => (row.Fields.GetValueOrDefault(keyField) as ValueFieldDataBase<string>)?.Value ?? string.Empty;
                var owners = rows.Select(KeyOf).Where(e => e.Length > 0).Distinct().ToList();
                var links = owners.Count == 0 ? new List<ModuleData>() : await ReadAllAsync(read, LinkRowsCondition(binding, field, owners));
                var byOwner = links.GroupBy(e => OwnerId(e, binding)).ToDictionary(g => g.Key, g => g.ToList());
                foreach (var row in rows)
                    row.Fields[field.Name] = new ListFieldData { Children = byOwner.TryGetValue(KeyOf(row), out var mine) ? mine : new() };
            }
        }

        /// <summary>TagField のデータ (タグ付け行) のタグ名 (行の順)。</summary>
        internal static List<string> TagNames(DesignData? design, TagFieldDesign field, ListFieldData data)
        {
            var binding = design == null ? null : Resolve(design, field);
            return data.GetModules()
                .Select(row => binding != null
                    ? TagName(row, binding)
                    : row.Fields.Values.OfType<TextFieldData>().FirstOrDefault()?.Value ?? string.Empty)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();
        }

        /// <summary>タグ付け行のタグ名 (無ければ空)。</summary>
        internal static string TagName(ModuleData linkRow, TagBinding binding)
            => (linkRow.Fields.GetValueOrDefault(binding.TagNameField) as TextFieldData)?.Value ?? string.Empty;

        /// <summary>タグ付け行の、タグを付けたレコードの Id。</summary>
        internal static string OwnerId(ModuleData linkRow, TagBinding binding)
            => (linkRow.Fields.GetValueOrDefault(binding.OwnerIdField) as ValueFieldDataBase<string>)?.Value ?? string.Empty;

        /// <summary>"Id.Value" の形の変数が指すフィールド名 (それ以外の形は空)。</summary>
        internal static string FieldOfVariable(string variable)
            => variable.EndsWith(".Value", StringComparison.Ordinal) && variable.IndexOf('.') == variable.Length - ".Value".Length
                ? variable[..^".Value".Length]
                : string.Empty;
    }
}
