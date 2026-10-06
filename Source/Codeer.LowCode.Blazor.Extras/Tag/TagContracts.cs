using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Utils;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Tag
{
    /// <summary>
    /// TagField の結び付き。タグ付けモジュール (TagLinkContractField) とタグのマスタ (TagContractField) の役割を、実際のフィールド名に解決したもの。
    /// 結び付きなし (<see cref="IsBound"/> = false) は保存しない入力欄 (マスタから候補を出すだけ)。
    /// </summary>
    internal sealed class TagBinding
    {
        /// <summary>タグ付けモジュール (結び付きなしなら空)。</summary>
        public string LinkModule { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグを付けたレコードの Id のフィールド。</summary>
        public string OwnerIdField { get; init; } = string.Empty;
        /// <summary>タグ付けモジュールの、タグへのリンクのフィールド。</summary>
        public string TagLinkField { get; init; } = string.Empty;
        /// <summary>タグを付けるレコードの側の、OwnerId に入る値の変数 (通常 "Id.Value")。</summary>
        public string OwnerKeyVariable { get; init; } = string.Empty;
        /// <summary>タグのマスタ。</summary>
        public string MasterModule { get; init; } = string.Empty;
        /// <summary>マスタのタグ名のフィールド。</summary>
        public string MasterNameField { get; init; } = string.Empty;

        public bool IsBound => !string.IsNullOrEmpty(LinkModule);
    }

    internal static class TagContracts
    {
        /// <summary>
        /// TagField の設定から結び付きを解決する。欠けていれば null (デザインチェックが指摘する不備)。
        /// 検索条件のモジュールがタグ付けモジュール = 結び付きあり。空ならマスタ (TagModuleName) だけの入力欄。
        /// </summary>
        internal static TagBinding? Resolve(DesignData design, TagFieldDesign field)
        {
            var linkModuleName = field.SearchCondition?.ModuleName ?? string.Empty;
            if (string.IsNullOrEmpty(linkModuleName))
            {
                var master = MasterContract(design.Modules.Find(field.TagModuleName));
                return master == null ? null : new TagBinding { MasterModule = field.TagModuleName, MasterNameField = master.TagName };
            }

            var linkModule = design.Modules.Find(linkModuleName);
            var link = LinkContract(linkModule);
            if (linkModule == null || link == null) return null;
            if (linkModule.Fields.FirstOrDefault(e => e.Name == link.Tag) is not LinkFieldDesign tagLink) return null;
            var masterName = tagLink.SearchCondition.ModuleName;
            var masterContract = MasterContract(design.Modules.Find(masterName));
            var ownerKey = OwnerKeyVariable(field.SearchCondition!, link.OwnerId);
            if (masterContract == null || string.IsNullOrEmpty(ownerKey)) return null;
            return new TagBinding
            {
                LinkModule = linkModuleName,
                OwnerIdField = link.OwnerId,
                TagLinkField = link.Tag,
                OwnerKeyVariable = ownerKey,
                MasterModule = masterName,
                MasterNameField = masterContract.TagName,
            };
        }

        internal static TagContractFieldDesign? MasterContract(ModuleDesign? module)
            => module?.Fields.OfType<TagContractFieldDesign>().FirstOrDefault();

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
                SelectFields = new List<string> { SystemFieldNames.Id, binding.OwnerIdField, binding.TagLinkField },
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
        /// 行たち (同じモジュール) に、結び付きありの TagField のタグ付け行を入れる (一覧の読み込みは子の一覧を含まないため。サーバーの再索引で使う)。
        /// fieldNames に無い TagField は触らない。
        /// </summary>
        internal static async Task FillTagRowsAsync(DesignData design, ModuleDesign module, IReadOnlyList<ModuleData> rows, IEnumerable<string> fieldNames,
            Func<SearchCondition, int, Task<Paging<ModuleData>?>> read)
        {
            var names = fieldNames.ToHashSet();
            foreach (var field in module.Fields.OfType<TagFieldDesign>().Where(e => names.Contains(e.Name)))
            {
                var binding = Resolve(design, field);
                if (binding == null || !binding.IsBound) continue;
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
                    : row.Fields.Values.OfType<LinkFieldData>().FirstOrDefault()?.DisplayText ?? string.Empty)
                .Where(e => !string.IsNullOrEmpty(e))
                .ToList();
        }

        /// <summary>タグ付け行のタグ名 (リンクの表示文字列。無ければ空)。</summary>
        internal static string TagName(ModuleData linkRow, TagBinding binding)
            => (linkRow.Fields.GetValueOrDefault(binding.TagLinkField) as LinkFieldData)?.DisplayText ?? string.Empty;

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
