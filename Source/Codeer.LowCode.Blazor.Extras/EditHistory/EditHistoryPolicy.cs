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
        /// </summary>
        internal static IEnumerable<(string Path, ModuleDesign Module, FieldDesignBase Field, OwnedRecordsDesign Owned, ModuleDesign? Child)> Walk(
            DesignData designData, ModuleDesign module, EditHistoryFieldDesign? field, bool descendIntoNotIncluded = false)
            => WalkCore(designData, module, field, descendIntoNotIncluded, string.Empty, new HashSet<string> { module.Name });

        static IEnumerable<(string Path, ModuleDesign Module, FieldDesignBase Field, OwnedRecordsDesign Owned, ModuleDesign? Child)> WalkCore(
            DesignData designData, ModuleDesign module, EditHistoryFieldDesign? field, bool descendIntoNotIncluded, string prefix, HashSet<string> visiting)
        {
            foreach (var (f, owned) in EditHistoryContracts.OwnedRecords(module))
            {
                var path = Path(prefix, owned.Name);
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                yield return (path, module, f, owned, child);
                if (child == null || visiting.Contains(child.Name)) continue;
                if (!descendIntoNotIncluded && !IsIncluded(field, path)) continue;
                foreach (var e in WalkCore(designData, child, field, descendIntoNotIncluded, path, new HashSet<string>(visiting) { child.Name }))
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
            StripCore(designData, field, design, snapshot, string.Empty, new HashSet<string> { design.Name });
            return snapshot;
        }

        static void StripCore(DesignData designData, EditHistoryFieldDesign field, ModuleDesign design, ModuleData data, string prefix, HashSet<string> visiting)
        {
            foreach (var (_, owned) in EditHistoryContracts.OwnedRecords(design))
            {
                var path = Path(prefix, owned.Name);
                if (!IsIncluded(field, path))
                {
                    data.Fields.Remove(owned.Name);
                    continue;
                }
                var child = designData.Modules.Find(owned.Condition.ModuleName);
                if (child == null || visiting.Contains(child.Name)) continue;
                if (data.Fields.GetValueOrDefault(owned.Name) is ListFieldData rows)
                {
                    foreach (var row in rows.Children) StripCore(designData, field, child, row, path, new HashSet<string>(visiting) { child.Name });
                }
            }
        }
    }
}
