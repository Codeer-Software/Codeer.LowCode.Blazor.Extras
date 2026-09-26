using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 2 つのスナップショットの差分。履歴一覧の「変更されたフィールドだけ 旧 → 新」表示用。
    /// 従属レコード (IOwnedRecordsFieldDesign の宣言) は行 Id で突き合わせて 追加 / 削除 / 変更 を出す。
    /// 従属でない一覧はスナップショットに含まれないので対象外。
    /// </summary>
    internal static class EditHistoryDiff
    {
        const int MaxTextLength = 300;

        /// <param name="canRead">閲覧権限のないフィールドを差分に出さない (フィールド名 → 読めるか)。</param>
        internal static List<EditHistoryChange> Compute(DesignData designData, ModuleDesign design,
            ModuleData? before, ModuleData after, Func<string, bool> canRead)
            => Compute(designData, design, before, after, canRead, new HashSet<string> { design.Name });

        /// <param name="excluded">出さないフィールド (明細行では親へのバインド条件のフィールド = 親のリンク)。</param>
        static List<EditHistoryChange> Compute(DesignData designData, ModuleDesign design,
            ModuleData? before, ModuleData after, Func<string, bool> canRead, HashSet<string> visiting, HashSet<string>? excluded = null)
        {
            var result = new List<EditHistoryChange>();
            foreach (var fieldDesign in design.Fields)
            {
                var name = fieldDesign.Name;
                if (EditHistoryContracts.IsExcludedField(name) || !canRead(name)) continue;
                if (excluded?.Contains(name) == true) continue;

                //従属レコード (宣言したフィールド: 明細の一覧・ガントチャート等) は行の追加・削除・変更で比べる
                if (fieldDesign is IOwnedRecordsFieldDesign owner)
                {
                    foreach (var owned in owner.GetOwnedRecords())
                    {
                        var childDesign = designData.Modules.Find(owned.Condition.ModuleName);
                        if (childDesign == null || visiting.Contains(childDesign.Name)) continue;
                        after.Fields.TryGetValue(owned.Name, out var oa);
                        FieldDataBase? ob = null;
                        before?.Fields.TryGetValue(owned.Name, out ob);
                        if (oa == null && ob == null) continue;
                        //親へのバインド条件のフィールド (親のリンク) は行の値として出さない
                        var bindFields = owned.Condition.GetFieldVariableConditions()
                            .Select(e => new VariableName(e.SearchTargetVariable).FieldName.FullName).ToHashSet();
                        var rows = CompareRows(designData, childDesign, (ob as ListFieldData)?.Children, (oa as ListFieldData)?.Children,
                            canRead, new HashSet<string>(visiting) { childDesign.Name }, bindFields);
                        if (rows.Count == 0) continue;
                        result.Add(new EditHistoryChange
                        {
                            FieldName = owned.Name,
                            DisplayName = owned.Name == name ? EditHistoryValues.DisplayName(fieldDesign) : owned.Name,
                            IsList = true, Rows = rows,
                        });
                    }
                    continue;
                }

                after.Fields.TryGetValue(name, out var a);
                FieldDataBase? b = null;
                before?.Fields.TryGetValue(name, out b);
                if (a == null && b == null) continue;
                if (a is ListFieldData || b is ListFieldData) continue;
                if (!EditHistoryValues.IsTextSupported(a) || !EditHistoryValues.IsTextSupported(b))
                {
                    //文字列にできない型: 変わったかどうかだけ (JSON で比較)。内容は「この版を表示」で見る
                    if (before != null && ToJson(a) == ToJson(b)) continue;
                    result.Add(new EditHistoryChange
                    {
                        FieldName = name, DisplayName = EditHistoryValues.DisplayName(fieldDesign), HasValueText = false,
                    });
                    continue;
                }
                var beforeText = Truncate(EditHistoryValues.Format(fieldDesign, b, designData));
                var afterText = Truncate(EditHistoryValues.Format(fieldDesign, a, designData));
                if (before == null ? afterText.Length == 0 : Equals(a, b) || beforeText == afterText) continue;
                result.Add(new EditHistoryChange
                {
                    FieldName = name, DisplayName = EditHistoryValues.DisplayName(fieldDesign), Before = beforeText, After = afterText,
                });
            }
            return result;
        }

        //明細の行差分。行は Id で突き合わせ、行の見分けは行番号 (推測で名前を決めない)
        static List<EditHistoryRowChange> CompareRows(DesignData designData, ModuleDesign childDesign,
            List<ModuleData>? before, List<ModuleData>? after, Func<string, bool> canRead, HashSet<string> visiting, HashSet<string> excluded)
        {
            var result = new List<EditHistoryRowChange>();
            before ??= new();
            after ??= new();
            var beforeById = ToDictionary(before);
            var afterById = ToDictionary(after);

            for (var i = 0; i < after.Count; i++)
            {
                var row = after[i];
                var id = EditHistorySnapshot.GetId(row);
                if (id.Length == 0 || !beforeById.TryGetValue(id, out var old))
                {
                    result.Add(new EditHistoryRowChange
                    {
                        Kind = EditHistoryRowChangeKind.Added, RowNumber = i + 1, Row = row,
                        Changes = Compute(designData, childDesign, null, row, canRead, visiting, excluded),
                    });
                    continue;
                }
                var changes = Compute(designData, childDesign, old, row, canRead, visiting, excluded);
                if (changes.Count == 0) continue;
                result.Add(new EditHistoryRowChange { Kind = EditHistoryRowChangeKind.Changed, RowNumber = i + 1, Row = row, Changes = changes });
            }
            for (var i = 0; i < before.Count; i++)
            {
                var row = before[i];
                var id = EditHistorySnapshot.GetId(row);
                if (id.Length != 0 && afterById.ContainsKey(id)) continue;
                //削除された行の値は Before 側に入れる (表示は打ち消し)
                var values = Compute(designData, childDesign, null, row, canRead, visiting, excluded);
                foreach (var v in values) { v.Before = v.After; v.After = string.Empty; }
                result.Add(new EditHistoryRowChange { Kind = EditHistoryRowChangeKind.Removed, RowNumber = i + 1, Row = row, Changes = values });
            }
            return result;
        }

        static Dictionary<string, ModuleData> ToDictionary(List<ModuleData> rows)
        {
            var dic = new Dictionary<string, ModuleData>();
            foreach (var row in rows)
            {
                var id = EditHistorySnapshot.GetId(row);
                if (id.Length != 0) dic.TryAdd(id, row);
            }
            return dic;
        }

        static string ToJson(FieldDataBase? data) => data == null ? string.Empty : JsonConverterEx.SerializeObject(data, false);

        static string Truncate(string text, int max = MaxTextLength)
            => text.Length <= max ? text : text[..max] + "…";
    }
}
