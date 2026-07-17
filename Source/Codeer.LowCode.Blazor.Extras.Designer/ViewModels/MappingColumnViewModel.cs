using System.ComponentModel;
using System.Runtime.CompilerServices;
using Codeer.LowCode.Blazor.Extras.Designs;

namespace Codeer.LowCode.Blazor.Extras.Designer.ViewModels
{
    public class MappingColumnViewModel : INotifyPropertyChanged
    {
        private readonly Func<string, List<string>> _conversionFieldCandidates;
        private int _number;

        public MappingColumnViewModel(MappingColumn model, Func<string, List<string>> conversionFieldCandidates)
        {
            Model = model;
            _conversionFieldCandidates = conversionFieldCandidates;
        }

        public MappingColumn Model { get; }

        //ファイルの列位置 (1 始まり)。親が並び替え/追加/削除時に振り直す
        public int Number
        {
            get => _number;
            set { if (value == _number) return; _number = value; OnPropertyChanged(nameof(ListLabel)); }
        }

        //列一覧の表示名 "位置: 名前"
        public string ListLabel
        {
            get
            {
                var name = !string.IsNullOrEmpty(Model.ExternalName) ? Model.ExternalName
                    : !string.IsNullOrEmpty(Model.Field) ? Model.Field
                    : Properties.Resources.MappingUnnamedColumn;
                return $"{_number}: {name}";
            }
        }

        public string ExternalName
        {
            get => Model.ExternalName;
            set
            {
                if (value == Model.ExternalName) return;
                Model.ExternalName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ListLabel));
            }
        }

        public string Field
        {
            get => Model.Field;
            set
            {
                if (value == Model.Field) return;
                Model.Field = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ListLabel));
            }
        }

        public string FixedValue
        {
            get => Model.FixedValue;
            set { if (value == Model.FixedValue) return; Model.FixedValue = value; OnPropertyChanged(); }
        }

        public string ConversionModule
        {
            get => Model.ConversionModule;
            set
            {
                if (value == Model.ConversionModule) return;
                Model.ConversionModule = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ConversionFieldCandidates));
            }
        }

        //変換表モジュールのフィールド候補 (モジュール変更で更新)
        public List<string> ConversionFieldCandidates => _conversionFieldCandidates(Model.ConversionModule);

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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
