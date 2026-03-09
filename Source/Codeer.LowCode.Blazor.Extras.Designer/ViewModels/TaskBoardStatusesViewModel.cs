using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Models;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class TaskBoardStatusesViewModel : INotifyPropertyChanged
    {
        private readonly TaskBoardStatuses _value;

        public TaskBoardStatusesViewModel(TaskBoardStatuses value)
        {
            _value = value;
            foreach (var item in value.Items)
            {
                Items.Add(new TaskBoardStatusViewModel(this, item));
            }
        }

        public ObservableCollection<TaskBoardStatusViewModel> Items { get; } = [];

        public event PropertyChangedEventHandler? PropertyChanged;

        public void Add(TaskBoardStatusDesign model)
        {
            Items.Add(new TaskBoardStatusViewModel(this, model));
            _value.Items.Add(model);
        }

        public void Remove(TaskBoardStatusDesign model)
        {
            var item = Items.FirstOrDefault(e => e.Model == model);
            if (item == null) return;

            Items.Remove(item);
            _value.Items.Remove(model);
        }

        public void MoveUp(TaskBoardStatusViewModel item)
        {
            var index = Items.IndexOf(item);
            if (index <= 0) return;
            Items.Move(index, index - 1);
            _value.Items.RemoveAt(index);
            _value.Items.Insert(index - 1, item.Model);
        }

        public void MoveDown(TaskBoardStatusViewModel item)
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
