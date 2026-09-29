using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
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

        /// <param name="canRead">閲覧権限のないフィールドを差分に出さない (フィールド名 → 読めるか)。対象モジュールと明細行のモジュールを区別しない。</param>
        internal static List<EditHistoryChange> Compute(DesignData designData, ModuleDesign design,
            ModuleData? before, ModuleData after, Func<string, bool> canRead)
            => Compute(designData, design, before, after, (_, name) => canRead(name));

        /// <param name="canRead">閲覧権限のないフィールドを差分に出さない (フィールドのモジュール, フィールド名 → 読めるか)。明細行の項目は行のモジュールで問われる。</param>
        internal static List<EditHistoryChange> Compute(DesignData designData, ModuleDesign design,
            ModuleData? before, ModuleData after, Func<ModuleDesign, string, bool> canRead)
            => Compute(designData, design, before, after, canRead, null);

        /// <param name="excluded">出さないフィールド (明細行では親へのバインド条件のフィールド = 親のリンク)。</param>
        //スナップショットは木 (記録側が同じレコードを二度入れない) なので、構造どおりに辿る (自己参照の従属も深さのまま比べる)
        static List<EditHistoryChange> Compute(DesignData designData, ModuleDesign design,
            ModuleData? before, ModuleData after, Func<ModuleDesign, string, bool> canRead, HashSet<string>? excluded)
        {
            var result = new List<EditHistoryChange>();
            foreach (var fieldDesign in design.Fields)
            {
                var name = fieldDesign.Name;
                if (EditHistoryContracts.IsExcludedField(name) || !canRead(design, name)) continue;
                if (excluded?.Contains(name) == true) continue;
                //承認フローの FK は承認の command API (サーバー) だけが、申請書の保存とは別のタイミングで書く。
                //申請・承認の記録は承認モジュール側の履歴にあるので、ここでは差分に出さない
                if (fieldDesign is ApprovalFlowFieldDesign) continue;

                //従属レコード (宣言したフィールド: 明細の一覧・ガントチャート等) は行の追加・削除・変更で比べる
                if (fieldDesign is IOwnedRecordsFieldDesign owner)
                {
                    foreach (var owned in owner.GetOwnedRecords())
                    {
                        var childDesign = designData.Modules.Find(owned.Condition.ModuleName);
                        if (childDesign == null) continue;
                        //行の内容を持たない側 (項目が無い・参照だけで内容が見えない) は行なしとして比べる
                        var oa = after.GetOwnedRows(owned.Name);
                        var ob = before?.GetOwnedRows(owned.Name);
                        if (oa == null && ob == null) continue;
                        //親へのバインド条件のフィールド (親のリンク) は行の値として出さない
                        var bindFields = owned.Condition.GetFieldVariableConditions()
                            .Select(e => new VariableName(e.SearchTargetVariable).FieldName.FullName).ToHashSet();
                        var rows = CompareRows(designData, childDesign, ob, oa, canRead, bindFields);
                        if (rows.Count == 0) continue;
                        result.Add(new EditHistoryChange
                        {
                            FieldName = owned.Name,
                            DisplayName = OwnedDisplayName(fieldDesign, owned.Name),
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
                //比較は元の文字列で行う (表示用に切り詰めた文字列で比べると、切り詰めた先だけの変更が「変更なし」になる)
                var beforeText = EditHistoryValues.Format(fieldDesign, b, designData);
                var afterText = EditHistoryValues.Format(fieldDesign, a, designData);
                if (before == null ? afterText.Length == 0 : Equals(a, b) || beforeText == afterText) continue;
                result.Add(new EditHistoryChange
                {
                    FieldName = name, DisplayName = EditHistoryValues.DisplayName(fieldDesign),
                    Before = Truncate(beforeText), After = Truncate(afterText),
                });
            }
            return result;
        }

        //従属レコードの宣言の表示名。宣言 = フィールド自身ならフィールドの表示名、
        //フィールドが自分の名前に接尾辞を付けた宣言 (Gantt の依存関係 "Gantt:Dependencies" 等) なら表示名 + 接尾辞
        static string OwnedDisplayName(FieldDesignBase fieldDesign, string ownedName)
        {
            if (ownedName == fieldDesign.Name) return EditHistoryValues.DisplayName(fieldDesign);
            return ownedName.StartsWith(fieldDesign.Name + ":")
                ? EditHistoryValues.DisplayName(fieldDesign) + ownedName[fieldDesign.Name.Length..]
                : ownedName;
        }

        //明細の行差分。行は Id で突き合わせ、行の見分けは行番号 (推測で名前を決めない)
        static List<EditHistoryRowChange> CompareRows(DesignData designData, ModuleDesign childDesign,
            IReadOnlyList<ModuleData>? before, IReadOnlyList<ModuleData>? after, Func<ModuleDesign, string, bool> canRead, HashSet<string> excluded)
        {
            var result = new List<EditHistoryRowChange>();
            before ??= [];
            after ??= [];
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
                        Changes = Compute(designData, childDesign, null, row, canRead, excluded),
                    });
                    continue;
                }
                var changes = Compute(designData, childDesign, old, row, canRead, excluded);
                if (changes.Count == 0) continue;
                result.Add(new EditHistoryRowChange { Kind = EditHistoryRowChangeKind.Changed, RowNumber = i + 1, Row = row, Changes = changes });
            }
            for (var i = 0; i < before.Count; i++)
            {
                var row = before[i];
                var id = EditHistorySnapshot.GetId(row);
                if (id.Length != 0 && afterById.ContainsKey(id)) continue;
                //削除された行の値は Before 側に入れる (表示は打ち消し)。行の中の従属レコード (孫の明細・埋め込みの子) の行も打ち消し
                var values = Compute(designData, childDesign, null, row, canRead, excluded);
                MarkRemoved(values);
                result.Add(new EditHistoryRowChange { Kind = EditHistoryRowChangeKind.Removed, RowNumber = i + 1, Row = row, Changes = values });
            }
            return result;
        }

        //「無かった → あった」として計算した内容を「あった → 無くなった」にする (値は Before 側へ、入れ子の行は Removed に)
        static void MarkRemoved(List<EditHistoryChange> changes)
        {
            foreach (var v in changes)
            {
                if (v.IsList)
                {
                    foreach (var row in v.Rows) { row.Kind = EditHistoryRowChangeKind.Removed; MarkRemoved(row.Changes); }
                    continue;
                }
                v.Before = v.After; v.After = string.Empty;
            }
        }

        static Dictionary<string, ModuleData> ToDictionary(IReadOnlyList<ModuleData> rows)
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
