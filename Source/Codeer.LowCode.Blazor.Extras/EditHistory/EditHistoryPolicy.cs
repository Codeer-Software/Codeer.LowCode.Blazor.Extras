using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

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
        /// 親の版に含める従属レコードの持ち主 1 つ: 履歴を持つモジュール (Root) の、宣言のパス (Path)。行を直接持つのは Owner の Field。
        /// Bindings は宣言の結び付き (行の項目 SearchTargetVariable = 親の項目 Variable)。行の値から親を引くのに使う。
        /// </summary>
        internal sealed record IncludedOwner(ModuleDesign Root, string Path, ModuleDesign Owner, FieldDesignBase Field, OwnedRecordsDesign Owned,
            List<FieldVariableMatchCondition> Bindings);

        /// <summary>
        /// 親の版に含める従属レコードのうち、行が親を指す宣言を、行のモジュール名 → 持ち主 で引く表
        /// (親は、別の画面で保存した行も版にする設定 RecordRowsSavedElsewhere の履歴つきのモジュールだけ)。
        /// 行のモジュールの画面などで行を保存したときに、その行を含む親の版を作るために使う。
        /// 見るのは宣言だけ (フィールドの型は見ない): 宣言の条件が「行の項目 = 親の項目」の結び付きを持っていれば、行の値から親を引ける。
        /// 同じ親 (Root) の中に持ち主が複数あるのはよい (明細と付属品が同じモジュール等)。
        /// 別の親 (Root) にも持ち主があるモジュールは設計の不備 (<see cref="HasMultipleRoots"/>。設計チェックが指摘し、保存も失敗にする)。
        /// 入らない宣言: 親が行を参照する宣言 (IsReferencedByOwner = 埋め込みモジュール。行は親の外にも居る別のレコードで、参照する親が何件あるか分からない)、
        /// 結び付きが無い・等しい以外の比較を含む宣言、リンク越し (ドット名) のフィールドの宣言、
        /// 親まで行をたどれない宣言 (途中に入らない宣言がある。埋め込みモジュールの中の一覧等)。
        /// </summary>
        internal static Dictionary<string, List<IncludedOwner>> IncludedOwners(DesignData designData)
        {
            var result = new Dictionary<string, List<IncludedOwner>>();
            foreach (var root in designData.Modules.ToList())
            {
                var field = EditHistoryContracts.Field(root);
                //別の画面で保存した行を版にしない親は、行から引かない (その親の画面からの保存だけが版になる)
                if (field == null || !field.RecordRowsSavedElsewhere) continue;
                var candidates = new List<(string Child, IncludedOwner Owner)>();
                foreach (var (path, module, f, owned, child) in Walk(designData, root, field))
                {
                    if (child == null || !IsIncluded(field, path)) continue;
                    if (owned.IsReferencedByOwner || EditHistoryContracts.IsExcludedField(f.Name)) continue;
                    var bindings = Bindings(owned.Condition);
                    if (bindings == null) continue;
                    if (candidates.Any(e => e.Owner.Owner.Name == module.Name && e.Owner.Owned.Name == owned.Name)) continue;
                    candidates.Add((child.Name, new IncludedOwner(root, path, module, f, owned, bindings)));
                }
                //持ち主のモジュールが Root までたどれる宣言だけを残す (Root の宣言から順に、たどれると分かったモジュールを広げる。Walk の列挙順には頼らない)
                var reachable = new HashSet<string> { root.Name };
                var rest = candidates;
                while (true)
                {
                    var found = rest.Where(e => reachable.Contains(e.Owner.Owner.Name)).ToList();
                    if (found.Count == 0) break;
                    rest = rest.Except(found).ToList();
                    foreach (var (childName, owner) in found)
                    {
                        if (!result.TryGetValue(childName, out var owners)) result[childName] = owners = new();
                        owners.Add(owner);
                        reachable.Add(childName);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 宣言の条件から、行の項目と親の項目の結び付き (And で並んだ「行の項目 = 親の項目」) を取り出す。
        /// 現在のユーザーの値を当てる条件 (CurrentUser.〜) は結び付きではないので除く。
        /// 結び付きが無い、または結び付きとして読めない条件 (Or や否定の中の変数条件・等しい以外の比較) があれば null (行から親を引けない宣言)。
        /// </summary>
        static List<FieldVariableMatchCondition>? Bindings(SearchCondition condition)
        {
            static bool IsParentVariable(FieldVariableMatchCondition e) => !e.Variable.StartsWith(SystemFieldNames.CurrentUser + ".", StringComparison.Ordinal);
            var bindings = new List<FieldVariableMatchCondition>();
            Collect(condition.Condition);
            bindings = bindings.Where(IsParentVariable).ToList();
            if (bindings.Count == 0 || bindings.Any(e => e.Comparison != MatchComparison.Equal)) return null;
            return bindings.Count == condition.GetFieldVariableConditions().Count(IsParentVariable) ? bindings : null;

            void Collect(MatchConditionBase? node)
            {
                if (node is FieldVariableMatchCondition variable) bindings.Add(variable);
                else if (node is MultiMatchCondition { IsOrMatch: false, IsNot: false } multi) multi.Children.ForEach(Collect);
            }
        }

        /// <summary>行のモジュールを版に含める親 (Root) が 2 つ以上あるか (行を保存したとき、どの親の版にするか決まらない)。</summary>
        internal static bool HasMultipleRoots(List<IncludedOwner> owners)
            => owners.Select(e => e.Root.Name).Distinct().Skip(1).Any();

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
