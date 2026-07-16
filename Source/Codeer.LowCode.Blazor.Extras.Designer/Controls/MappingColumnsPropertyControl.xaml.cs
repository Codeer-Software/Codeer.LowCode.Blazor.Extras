using System.Windows;
using System.Windows.Controls;
using Codeer.LowCode.Blazor.Extras.Designer.ViewModels;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designer.Controls
{
    public partial class MappingColumnsPropertyControl : UserControl,
        ICustomPropertyControl
    {
        private MappingColumns _value = new();
        private Action<bool> _completion = _ => { };
        private MappingColumnsViewModel _dataContext = null!;

        public object? Value => _value;

        public MappingColumnsPropertyControl()
        {
            InitializeComponent();
        }

        public void Initialize(CustomPropertyItemInfo propertyItemInfo, object? value,
            Action<bool> completion)
        {
            _value = (value as MappingColumns) ?? new();
            _completion = completion;
            DataContext = _dataContext = new MappingColumnsViewModel(_value, propertyItemInfo);
        }

        private void OkClick(object sender, RoutedEventArgs e)
            => _completion!(true);

        private void CancelClick(object sender, RoutedEventArgs e)
            => _completion!(false);

        private void AddItem(object sender, RoutedEventArgs e)
            => _dataContext.Add();

        private void DeleteItem(object sender, RoutedEventArgs e)
            => _dataContext.DeleteSelected();

        private void MoveUpItem(object sender, RoutedEventArgs e)
            => _dataContext.MoveSelected(-1);

        private void MoveDownItem(object sender, RoutedEventArgs e)
            => _dataContext.MoveSelected(1);
    }
}
