using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Runtime.CompilerServices;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// 子レコードを ModuleCollection で持つフィールド (Gantt / Calendar / TaskBoard / MarkerList) が、
    /// 本体の IOwnedRecordsField (行の出し入れ・与えられた行の表示) を実装するための部品。
    /// </summary>
    internal static class OwnedRecordModules
    {
        //表示専用で見せている行 (CreateForShowAsync) の項目に付けるクラス。
        //行モジュールの ClassName はレイアウトのデザインで付けたクラスを含むので、項目 (バー・予定・カード・マーカー) にはこちらだけを出す
        static readonly ConditionalWeakTable<Module, string> _shownClassNames = new();

        /// <summary>表示専用で見せている行の項目に付けるクラス (OwnedRecordRow.ClassName)。通常の行は空。</summary>
        internal static string ShownClassName(this Module? module)
            => module != null && _shownClassNames.TryGetValue(module, out var className) ? className : string.Empty;

        /// <summary>
        /// 行を 1 つ足す。id が null なら新しい行、あればその Id の既にある行 (内容は入れない)。
        /// どちらも親への束縛の値を入れる (既にある行を別の親の下に足したときも、その親を指す)。
        /// </summary>
        /// <param name="layoutType">行のモジュールを作るレイアウト種別 (画面を持たない行は None)。</param>
        internal static async Task<Module> AddAsync(FieldBase field, ModuleCollection collection, SearchCondition condition, string layoutName,
            string? id, ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            Module module;
            if (id == null)
            {
                module = await field.CreateChildModuleAsync(condition.ModuleName, layoutType, layoutName);
            }
            else
            {
                //行データごと作る (Id が実 Id なので新しい行の扱いにならない)
                var data = new ModuleData { Name = condition.ModuleName };
                data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = id };
                module = await ModuleCreationService.CreateModuleAsync(field.Services, data, layoutType, layoutName);
            }
            await field.AssignConditionValuesAsync(condition, module);
            collection.Add(module);
            return module;
        }

        /// <summary>与えられた行から表示専用のモジュールを作る (DB は読まない)。</summary>
        internal static async Task<List<Module>> CreateForShowAsync(FieldBase field, string layoutName, IReadOnlyList<OwnedRecordRow> rows,
            ModuleLayoutType layoutType = ModuleLayoutType.Detail)
        {
            var list = new List<Module>();
            foreach (var row in rows)
            {
                var module = await row.CreateModuleAsync(field.Services, layoutType, layoutName);
                if (!string.IsNullOrEmpty(row.ClassName)) _shownClassNames.AddOrUpdate(module, row.ClassName);
                list.Add(module);
            }
            return list;
        }
    }
}
