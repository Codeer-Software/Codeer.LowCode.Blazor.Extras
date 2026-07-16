using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class MappingColumnsViewModel : INotifyPropertyChanged
    {
        private readonly MappingColumns _value;

        public MappingColumnsViewModel(MappingColumns value)
        {
            _value = value;
            foreach (var item in value.Items)
            {
                Items.Add(new MappingColumnViewModel(this, item));
            }
        }

        public ObservableCollection<MappingColumnViewModel> Items { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Add(MappingColumn model)
        {
            Items.Add(new MappingColumnViewModel(this, model));
            _value.Items.Add(model);
        }

        public void Remove(MappingColumn model)
        {
            var item = Items.FirstOrDefault(e => e.Model == model);
            if (item == null) return;

            Items.Remove(item);
            _value.Items.Remove(model);
        }

        public void MoveUp(MappingColumnViewModel item)
        {
            var index = Items.IndexOf(item);
            if (index <= 0) return;
            Items.Move(index, index - 1);
            _value.Items.RemoveAt(index);
            _value.Items.Insert(index - 1, item.Model);
        }

        public void MoveDown(MappingColumnViewModel item)
        {
            var index = Items.IndexOf(item);
            if (index < 0 || index >= Items.Count - 1) return;
            Items.Move(index, index + 1);
            _value.Items.RemoveAt(index);
            _value.Items.Insert(index + 1, item.Model);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
