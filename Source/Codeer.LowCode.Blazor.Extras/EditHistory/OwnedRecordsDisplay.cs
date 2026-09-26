using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.EditHistory
{
    /// <summary>
    /// 版表示ダイアログ用: 版の行から表示専用のモジュールを作る (DB は読まない)。
    /// </summary>
    internal static class OwnedRecordsDisplay
    {
        internal static async Task<List<Module>> CreateAsync(FieldBase field, string moduleName, string layoutName, List<ModuleData> rows)
        {
            var list = new List<Module>();
            foreach (var row in rows)
            {
                //行データごと作る (Id が実 Id なので新規扱いにならず「変更あり」にもならない。仮 Id で作って後から入れると
                //変更ありのまま残り、Gantt の週送りなどが「変更を破棄しますか」を出してしまう)
                var mod = await ModuleCreationService.CreateModuleAsync(field.Services, row, ModuleLayoutType.Detail, layoutName);
                mod.IsViewOnly = true;
                list.Add(mod);
            }
            return list;
        }
    }
}
