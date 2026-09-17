using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class MappingColumnsViewModel : INotifyPropertyChanged
    {
        private readonly MappingColumns _value;
        private MappingColumnViewModel? _selectedItem;

        public MappingColumnsViewModel(MappingColumns value, CustomPropertyItemInfo? propertyItemInfo)
        {
            _value = value;
            FieldCandidates = propertyItemInfo == null ? [] : BuildFieldCandidates(propertyItemInfo.ModuleDesign);

            foreach (var item in value.Items)
            {
                Items.Add(CreateItemViewModel(item));
            }
            Renumber();
            SelectedItem = Items.FirstOrDefault();
        }

        public ObservableCollection<MappingColumnViewModel> Items { get; } = [];

        //Field 候補 (自モジュールの "フィールド名.データメンバ名")
        public List<string> FieldCandidates { get; }

        public MappingColumnViewModel? SelectedItem
        {
            get => _selectedItem;
            set { if (value == _selectedItem) return; _selectedItem = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Add()
        {
            var model = new MappingColumn();
            _value.Items.Add(model);
            var item = CreateItemViewModel(model);
            Items.Add(item);
            Renumber();
            SelectedItem = item;
        }

        public void DeleteSelected()
        {
            var item = SelectedItem;
            if (item == null) return;

            var index = Items.IndexOf(item);
            Items.Remove(item);
            _value.Items.Remove(item.Model);
            Renumber();
            SelectedItem = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
        }

        public void MoveSelected(int offset)
        {
            var item = SelectedItem;
            if (item == null) return;

            var index = Items.IndexOf(item);
            var to = index + offset;
            if (to < 0 || Items.Count <= to) return;

            Items.Move(index, to);
            _value.Items.RemoveAt(index);
            _value.Items.Insert(to, item.Model);
            Renumber();
        }

        private MappingColumnViewModel CreateItemViewModel(MappingColumn model) => new(model);

        //一覧表示の列位置番号 (1 始まり = ファイルの列位置)
        private void Renumber()
        {
            for (var i = 0; i < Items.Count; i++) Items[i].Number = i + 1;
        }

        //Field 候補は取込本体と同じ規約 (DbColumn プロパティから "フィールド名.データメンバ名" を再構成)
        private static List<string> BuildFieldCandidates(ModuleDesign module)
            => module.Fields
                .SelectMany(f => GetAssignedDbColumns(f).Select(m => $"{f.Name}.{m}"))
                .ToList();

        private static IEnumerable<string> GetAssignedDbColumns(FieldDesignBase field)
        {
            foreach (var prop in field.GetType().GetProperties())
            {
                var attr = prop.GetCustomAttribute<DbColumnAttribute>();
                if (attr == null) continue;
                if (string.IsNullOrEmpty(prop.GetValue(field) as string)) continue; //DB列未割当
                yield return attr.DataMember;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
