using System.Windows;
using System.Windows.Controls;
using Codeer.LowCode.Blazor.Extras.Designer.ViewModels;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Extras.Fields;

namespace Codeer.LowCode.Blazor.Extras.Designer.Controls
{
    public partial class TaskBoardStatusesPropertyControl : UserControl,
        ICustomPropertyControl
    {
        private TaskBoardStatuses _value = new();
        private Action<bool> _completion = _ => { };
        private TaskBoardStatusesViewModel _dataContext = null!;

        public object? Value => _value;

        public TaskBoardStatusesPropertyControl()
        {
            InitializeComponent();
        }

        public void Initialize(CustomPropertyItemInfo propertyItemInfo, object? value,
            Action<bool> completion)
        {
            _value = (value as TaskBoardStatuses) ?? new();
            _completion = completion;
            DataContext = _dataContext = new TaskBoardStatusesViewModel(_value);
        }

        private void OkClick(object sender, RoutedEventArgs e)
            => _completion!(true);

        private void CancelClick(object sender, RoutedEventArgs e)
            => _completion!(false);

        private void DeleteItem(object sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: TaskBoardStatusViewModel item })
            {
                _dataContext.Remove(item.Model);
            }
        }

        private void AddItem(object sender, RoutedEventArgs e)
        {
            _dataContext.Add(new TaskBoardStatus());
        }
    }
}
