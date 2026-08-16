using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

namespace Codeer.LowCode.Blazor.Extras.Server.Mail
{
    /// <summary>
    /// 宛先・テンプレートのリンクパス ("Contact.Email" 等) をサーバー側で解決して行データへ注入する。
    /// データ層の SelectFields はモジュールに宣言済みのフィールドしか実体化しないため、
    /// 宣言なしのリンクパスはここでリンク先モジュールを一括ロードして補完する。
    /// </summary>
    public static class MailLinkPathLoader
    {
        /// <summary>rows に実体の無いリンクパスの値をリンク先モジュールから一括で引き、ドット名で row.Fields へ注入する。</summary>
        public static async Task LoadAsync(ModuleDataIO io, DesignData designData, ModuleDesign design,
            List<ModuleData> rows, IEnumerable<string> fieldPaths)
        {
            if (rows.Count == 0) return;
            foreach (var group in fieldPaths
                         .Where(e => !string.IsNullOrEmpty(e))
                         .Select(e => new FieldName(e))
                         .Where(e => e.IsLink)
                         .GroupBy(e => e.Root))
            {
                //宣言済みドット列などで全行に実体があるパスは触らない
                var missing = group.Where(p => rows.Any(r => !r.Fields.ContainsKey(p.FullName)))
                    .Select(e => e.FullName).Distinct().ToList();
                if (missing.Count == 0) continue;

                if (design.Fields.FirstOrDefault(e => e.Name == group.Key) is not LinkFieldDesign link) continue;
                var targetDesign = designData.Modules.Find(link.SearchCondition.ModuleName);
                if (targetDesign == null) continue;

                var fks = rows
                    .Select(r => (r.Fields.GetValueOrDefault(group.Key) as ValueFieldDataBase<string>)?.Value)
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Distinct().ToList();
                if (fks.Count == 0) continue;

                var valueVariable = string.IsNullOrEmpty(link.ValueVariable) ? "Id.Value" : link.ValueVariable;
                var keyPath = MailVariableResolver.ParseToken(valueVariable).FieldPath;
                var restPaths = missing.Select(e => new FieldName(e).SkipRoot().FullName).Distinct().ToList();
                var condition = new SearchCondition
                {
                    ModuleName = targetDesign.Name,
                    Condition = new MultiMatchCondition
                    {
                        IsOrMatch = true,
                        Children = fks.Select(fk => (MatchConditionBase)new FieldValueMatchCondition
                        {
                            SearchTargetVariable = valueVariable,
                            Comparison = MatchComparison.Equal,
                            Value = MultiTypeValue.Create(fk),
                        }).ToList(),
                    },
                    SelectFields = restPaths.Select(e => new FieldName(e).Root)
                        .Append(keyPath).Append(SystemFieldNames.Id).Distinct().ToList(),
                };
                var targetRows = (await io.GetListAsync(condition, 0)).Items;

                //さらに深いリンクパスは再帰で解決してから注入する
                await LoadAsync(io, designData, targetDesign, targetRows,
                    restPaths.Where(e => new FieldName(e).IsLink));

                var map = targetRows
                    .GroupBy(r => GetKeyText(r, keyPath))
                    .ToDictionary(g => g.Key, g => g.First());
                foreach (var row in rows)
                {
                    var fk = (row.Fields.GetValueOrDefault(group.Key) as ValueFieldDataBase<string>)?.Value;
                    if (string.IsNullOrEmpty(fk) || !map.TryGetValue(fk, out var target)) continue;
                    foreach (var rest in restPaths)
                    {
                        var path = $"{group.Key}.{rest}";
                        if (row.Fields.ContainsKey(path)) continue;
                        if (target.Fields.TryGetValue(rest, out var value) && value != null)
                            row.Fields[path] = value;
                    }
                }
            }
        }

        static string GetKeyText(ModuleData row, string keyPath)
            => row.Fields.GetValueOrDefault(keyPath) switch
            {
                ValueFieldDataBase<string> s => s.Value ?? string.Empty,
                var v => v?.GetType().GetProperty("Value")?.GetValue(v)?.ToString() ?? string.Empty,
            };
    }
}
