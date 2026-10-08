using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 従属レコードの扱い (EditHistoryFieldDesign の ExcludedOwnedRecords / IndividuallyRecordedOwnedRecords)。
    /// パス = 従属宣言の Name。子・孫・埋め込みの中は "Items.Details" のようにドット区切り。
    /// 含める (既定) = 親の版に入る。除外 = 履歴に含めない。行ごと = 親の版には入れず、行のモジュール自身の履歴に 1 行 1 版で記録する。
    /// 判断はここ (履歴の機能側) だけで行い、本体の宣言や一覧には持たせない。
    /// </summary>
    internal static class EditHistoryPolicy
    {
        internal static string Path(string prefix, string name) => prefix.Length == 0 ? name : $"{prefix}.{name}";

        internal static bool IsExcluded(EditHistoryFieldDesign? field, string path)
            => field != null && field.ExcludedOwnedRecords.Contains(path);

        internal static bool IsIndividual(EditHistoryFieldDesign? field, string path)
            => field != null && field.IndividuallyRecordedOwnedRecords.Contains(path);

        /// <summary>親の版に含めるか (除外でも行ごとでもない)。</summary>
        internal static bool IsIncluded(EditHistoryFieldDesign? field, string path)
            => !IsExcluded(field, path) && !IsIndividual(field, path);

        /// <summary>
        /// 従属宣言を子・孫・埋め込みの中まで列挙する (パス付き)。循環は止める (経路上に同じモジュールが出たら先へ行かない)。
        /// 含めない宣言の先は、descendIntoNotIncluded でなければ辿らない。
        /// 同じモジュールに複数の経路で辿り着くとき、その先に除外・行ごとの指定が無ければ、そのモジュールの中は最初の経路の 1 回だけ列挙する
        /// (指定が無い範囲はどの経路でも扱いが同じ。全経路を列挙すると、明細で互いに繋がった設計では経路の数だけ膨らむ)。
        /// </summary>
        internal static IEnumerable<(string Path, ModuleDesign Module, FieldDesignBase Field, OwnedRecordsDesign Owned, ModuleDesign? Child)> Walk(
            DesignData designData, ModuleDesign module, EditHistoryFieldDesign? field, bool descendIntoNotIncluded = false)
        {
            var declared = field == null ? [] : field.ExcludedOwnedRecords.Concat(field.IndividuallyRecordedOwnedRecords).Distinct().ToList();
            return WalkCore(designData, module, field, descendIntoNotIncluded, string.Empty, new HashSet<string> { module.Name }, declared, new HashSet<string>());
        }

        static IEnumerable<(string Path, ModuleDesign Module, FieldDesignBase Field, OwnedRecordsDesign Owned, ModuleDesign? Child)> WalkCore(
            DesignData designData, ModuleDesign module, EditHistoryFieldDesign? field, bool descendIntoNotIncluded, string prefix, HashSet<string> visiting,
            List<string> declared, HashSet<string> expanded)
        {
            foreach (var (f, owned) in EditHistoryContracts.OwnedRecords(designData.Modules, module))
            {
                var path = Path(prefix, owned.Name);
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                yield return (path, module, f, owned, child);
                if (child == null || visiting.Contains(child.Name)) continue;
                if (!descendIntoNotIncluded && !IsIncluded(field, path)) continue;
                //この先に指定 (除外・行ごとのパス) が無いなら、子のモジュールの中は 1 回だけ列挙する
                var hasDeclaredBelow = declared.Any(e => e.StartsWith(path + ".", StringComparison.Ordinal));
                if (!hasDeclaredBelow && !expanded.Add(child.Name)) continue;
                foreach (var e in WalkCore(designData, child, field, descendIntoNotIncluded, path, new HashSet<string>(visiting) { child.Name }, declared, expanded))
                    yield return e;
            }
        }

        /// <summary>
        /// 親の版に含めない宣言 (除外・行ごと) の先のモジュールと、その従属の子孫のモジュール名。
        /// その行だけの保存は親には変更が無いので親の版にしない範囲。
        /// </summary>
        internal static HashSet<string> NotIncludedModules(DesignData designData, ModuleDesign module, EditHistoryFieldDesign field)
        {
            var result = new HashSet<string>();
            foreach (var (path, _, _, _, child) in Walk(designData, module, field, descendIntoNotIncluded: true))
            {
                if (child == null || IsIncluded(field, path) || !result.Add(child.Name)) continue;
                foreach (var e in Walk(designData, child, null, descendIntoNotIncluded: true))
                {
                    if (e.Child != null) result.Add(e.Child.Name);
                }
            }
            return result;
        }

        /// <summary>
        /// 版のスナップショットから、含めない従属レコード (除外・行ごと) を取り除く。
        /// 記録側は最初から読まないが、除外・行ごとを指定する前に記録された版には入っているので、読む側で揃える (差分・版表示・復元が触らない)。
        /// </summary>
        internal static ModuleData? Strip(DesignData designData, EditHistoryFieldDesign? field, ModuleData? snapshot)
        {
            if (snapshot == null || field == null || (field.ExcludedOwnedRecords.Count == 0 && field.IndividuallyRecordedOwnedRecords.Count == 0)) return snapshot;
            var design = designData.Modules.Find(snapshot.Name);
            if (design == null) return snapshot;
            StripCore(designData, field, design, snapshot, string.Empty);
            return snapshot;
        }

        //スナップショットは木 (記録側が同じレコードを二度入れない) なので構造どおりに辿る (自己参照の従属も深さのまま)
        static void StripCore(DesignData designData, EditHistoryFieldDesign field, ModuleDesign design, ModuleData data, string prefix)
        {
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(designData.Modules, design))
            {
                var path = Path(prefix, owned.Name);
                if (!IsIncluded(field, path))
                {
                    data.Fields.Remove(owned.Name);
                    continue;
                }
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null) continue;
                foreach (var row in data.GetOwnedRows(owned.Name) ?? []) StripCore(designData, field, child, row, path);
            }
        }
    }
}
