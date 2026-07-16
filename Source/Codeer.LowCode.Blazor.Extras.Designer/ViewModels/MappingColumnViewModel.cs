using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class MappingColumnViewModel : INotifyPropertyChanged
    {
        private readonly MappingColumnsViewModel _parent;

        public MappingColumnViewModel(MappingColumnsViewModel parent, MappingColumn model)
        {
            _parent = parent;
            Model = model;
            MoveUpCommand = new DelegateCommand(() => _parent.MoveUp(this));
            MoveDownCommand = new DelegateCommand(() => _parent.MoveDown(this));
        }

        public MappingColumn Model { get; }

        public string ExternalName
        {
            get => Model.ExternalName;
            set { if (value == Model.ExternalName) return; Model.ExternalName = value; OnPropertyChanged(); }
        }

        public string Field
        {
            get => Model.Field;
            set { if (value == Model.Field) return; Model.Field = value; OnPropertyChanged(); }
        }

        public string Format
        {
            get => Model.Format;
            set { if (value == Model.Format) return; Model.Format = value; OnPropertyChanged(); }
        }

        public string FixedValue
        {
            get => Model.FixedValue;
            set { if (value == Model.FixedValue) return; Model.FixedValue = value; OnPropertyChanged(); }
        }

        public string ConversionModule
        {
            get => Model.ConversionModule;
            set { if (value == Model.ConversionModule) return; Model.ConversionModule = value; OnPropertyChanged(); }
        }

        public string ConversionExternalField
        {
            get => Model.ConversionExternalField;
            set { if (value == Model.ConversionExternalField) return; Model.ConversionExternalField = value; OnPropertyChanged(); }
        }

        public string ConversionInternalField
        {
            get => Model.ConversionInternalField;
            set { if (value == Model.ConversionInternalField) return; Model.ConversionInternalField = value; OnPropertyChanged(); }
        }

        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
