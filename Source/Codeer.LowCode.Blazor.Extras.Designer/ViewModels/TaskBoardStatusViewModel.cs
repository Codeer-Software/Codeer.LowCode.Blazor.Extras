using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Codeer.LowCode.Blazor.Extras.Designer.Interop;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class TaskBoardStatusViewModel : INotifyPropertyChanged
    {
        private readonly TaskBoardStatusesViewModel _parent;

        public TaskBoardStatusViewModel(TaskBoardStatusesViewModel parent, TaskBoardStatusDesign model)
        {
            _parent = parent;
            Model = model;
            ChooseColorCommand = new DelegateCommand(OpenColorPicker);
            MoveUpCommand = new DelegateCommand(() => _parent.MoveUp(this));
            MoveDownCommand = new DelegateCommand(() => _parent.MoveDown(this));
        }

        public TaskBoardStatusDesign Model { get; }

        public string DisplayText
        {
            get => Model.DisplayText;
            set
            {
                if (value == Model.DisplayText) return;
                Model.DisplayText = value;
                OnPropertyChanged();
            }
        }

        public string Value
        {
            get => Model.Value;
            set
            {
                if (value == Model.Value) return;
                Model.Value = value;
                OnPropertyChanged();
            }
        }

        public string Color
        {
            get => Model.Color;
            set
            {
                if (value == Model.Color) return;
                Model.Color = value;
                OnPropertyChanged();
            }
        }

        public bool CanAdd
        {
            get => Model.CanAdd;
            set
            {
                if (value == Model.CanAdd) return;
                Model.CanAdd = value;
                OnPropertyChanged();
            }
        }

        public ICommand ChooseColorCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OpenColorPicker()
        {
            var dialog = new ColorDialog
            {
                Color = HexStringToColor(Color)
            };
            if (dialog.ShowDialog() == true)
            {
                Color = ColorToHexString(dialog.Color);
            }
        }

        private static string ColorToHexString(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static Color HexStringToColor(string? hex)
        {
            if (string.IsNullOrEmpty(hex) || hex.Length != 7 || hex[0] != '#')
            {
                return Colors.Black;
            }

            var r = byte.Parse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            var g = byte.Parse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            var b = byte.Parse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }
    }
}
