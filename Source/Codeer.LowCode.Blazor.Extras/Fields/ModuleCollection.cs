using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class ModuleCollection
    {
        private readonly List<Module> _items = [];
        private readonly List<ModuleDeleteInfo> _deleted = [];

        public IReadOnlyList<Module> Items => _items;

        public bool IsModified => _deleted.Any() || _items.Any(e => e.IsModified);

        public void Add(Module module) => _items.Add(module);

        public void AddRange(IEnumerable<Module> modules) => _items.AddRange(modules);

        public void Remove(Module module)
        {
            if (!module.IsNewData)
                _deleted.Add(module.CreateModuleDeleteInfo());
            _items.Remove(module);
        }

        public void ApplyLoaded(IEnumerable<Module> modules)
        {
            _deleted.Clear();
            _items.Clear();
            _items.AddRange(modules);
        }

        public void Clear()
        {
            _deleted.AddRange(_items
                .Where(e => !e.IsNewData)
                .Select(e => e.CreateModuleDeleteInfo()));
            _items.Clear();
        }

        public FieldSubmitData GetSubmitData()
        {
            if (!IsModified) return new();

            var submitData = new FieldSubmitData();
            submitData.Delete.AddRange(_deleted.Where(x => !string.IsNullOrEmpty(x.Id)));

            foreach (var module in _items)
            {
                var data = module.GetSubmitData();
                submitData.Add.AddRange(data.Add);
                submitData.Update.AddRange(data.Update);
                submitData.Delete.AddRange(data.Delete);
                submitData.ExtendedData.AddRange(data.ExtendedData);
            }

            return submitData;
        }

        public FieldSubmitData GetSubmitData(IAppInfoService appInfoService, SearchCondition resolvedCondition)
        {
            if (!IsModified) return new();

            var submitData = new FieldSubmitData();
            submitData.Delete.AddRange(_deleted.Where(x => !string.IsNullOrEmpty(x.Id)));

            var dataList = new List<ModuleData>();
            foreach (var module in _items)
            {
                var data = module.GetSubmitData();
                submitData.Add.AddRange(data.Add);
                submitData.Update.AddRange(data.Update);
                submitData.Delete.AddRange(data.Delete);
                submitData.ExtendedData.AddRange(data.ExtendedData);

                dataList.AddRange(data.Add.Where(e => e.Name == data.ModuleName && GetIdText(e) == data.Id));
                dataList.AddRange(data.Update.Where(e => e.Name == data.ModuleName && GetIdText(e) == data.Id));
            }

            foreach (var moduleData in dataList)
            {
                SearchConditionHelper.AssignFromConditionValues(moduleData, appInfoService, resolvedCondition);
            }

            return submitData;
        }

        private static string GetIdText(ModuleData data)
            => data.Fields.TryGetValue(SystemFieldNames.Id, out var field) && field is IdFieldData idField
                ? idField.Value?.ToString() ?? string.Empty
                : string.Empty;
    }
}
