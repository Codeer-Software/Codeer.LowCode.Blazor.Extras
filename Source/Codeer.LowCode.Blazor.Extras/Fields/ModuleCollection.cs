using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Match;

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

        public async Task<bool> ValidateInput()
        {
            //全て確定させる必要があるのでToList()
            var results = new List<bool>();
            foreach (var e in Items)
            {
                results.Add(await e.ValidateInput());
            }
            if (!results.All(e => e)) return false;
            return true;
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

        public FieldSubmitData GetSubmitData(FieldBase field, SearchCondition searchCondition)
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

            field.AssignConditionValues(searchCondition, submitData.Add);
            return submitData;
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

    }

    internal static class ModuleHelper
    {
        public static async Task CopyFieldDataAsync(Module from, Module to)
        {
            foreach (var fromField in from.GetFields())
            {
                var toField = to.GetField(fromField.Design.Name);
                if (toField == null) continue;
                await toField.SetDataAsync(fromField.GetData());
            }
        }
    }
}
