using System;
using System.Collections.Generic;
using System.Linq;
using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace Codeer.LowCode.Blazor.Extras.Designer.Controls
{
    /// <summary>
    /// クロス集計フィールドの集計設定 (CrossTabSetting) のエディタ。行 / 列 / 値 / 値で絞り込み / 並べ替えと表示件数 をタブで編集する。
    /// 項目の候補は元モジュール (SearchCondition.ModuleName) の項目と、リンク先 1 段の項目 ("Customer.Name.Value")。
    /// OK で編集中の複製を値として返す (キャンセルは元のまま)。
    /// </summary>
    public partial class CrossTabSettingPropertyControl : UserControl, ICustomPropertyControl
    {
        CrossTabSetting? _value;
        Action<bool> _completion = _ => { };
        CrossTabSettingViewModel? _viewModel;

        object? ICustomPropertyControl.Value => _value;

        public CrossTabSettingPropertyControl() => InitializeComponent();

        public void Initialize(CustomPropertyItemInfo propertyItemInfo, object? value, Action<bool> completion)
        {
            _value = (value as CrossTabSetting)?.JsonClone() ?? new CrossTabSetting();
            _completion = completion;
            var moduleName = (propertyItemInfo.FieldDesign as CrossTabFieldDesign)?.SearchCondition.ModuleName ?? string.Empty;
            DataContext = _viewModel = new CrossTabSettingViewModel(propertyItemInfo.DesignData, moduleName, _value);
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.Width = 760;
                window.Height = 520;
                window.MinWidth = 560;
                window.MinHeight = 360;
            }
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            _viewModel?.Apply();
            _completion(true);
        }

        void CancelClick(object sender, RoutedEventArgs e) => _completion(false);
    }

    /// <summary>列挙 (集計方法・まとめる単位・比較・並べ替えの対象) を利用者向けの文言にする (Resources の CrossTabSetting_Enum_名前)。</summary>
    public class AggregateEnumTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            return Properties.Resources.ResourceManager.GetString("CrossTabSetting_Enum_" + value, Properties.Resources.Culture) ?? value.ToString();
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>値の書式の候補を「N1 — 小数 1 桁」のように見せる (選択後のテキストは書式そのもの)。</summary>
    public class CrossTabFormatTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var code = value?.ToString() ?? string.Empty;
            var text = CrossTabFormatPresets.TextOf(code);
            return text == code ? code : $"{code} — {text}";
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    /// <summary>番号で指す行・列・値を「番号: 名前」で選ばせるための項目。</summary>
    public record IndexItem(int Index, string Text);

    class CrossTabRelayCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }

    abstract class CrossTabItemViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        public ICommand RemoveCommand { get; protected set; } = new CrossTabRelayCommand(() => { });
    }

    class GroupItemViewModel : CrossTabItemViewModelBase
    {
        public GroupItemViewModel(CrossTabSettingViewModel owner, AggregateGroup model, Action remove)
        {
            Owner = owner;
            Model = model;
            RemoveCommand = new CrossTabRelayCommand(remove);
        }
        public const string NoneBucket = "None";

        public CrossTabSettingViewModel Owner { get; }
        //まとめ方が変わると型ごと差し替わる (ValueGroup ⇔ DateGroup)。Apply が読むのはこの Model
        public AggregateGroup Model { get; private set; }
        public string Variable
        {
            get => Model.Variable;
            set
            {
                Model.Variable = value ?? string.Empty;
                //日付でなくなったら 値そのまま に戻す
                if (Model is DateGroup && !IsDate) Model = new ValueGroup { Variable = Model.Variable };
                OnPropertyChanged(); OnPropertyChanged(nameof(IsDate)); OnPropertyChanged(nameof(Bucket)); OnPropertyChanged(nameof(IsFiscal)); Owner.Refresh();
            }
        }
        //まとめ方: "None" = 値そのまま、それ以外は DateBucket の名前 (まとめる単位。日付・日時の項目のときだけ出す)
        public string Bucket
        {
            get => Model is DateGroup d ? d.Bucket.ToString() : NoneBucket;
            set
            {
                if (Enum.TryParse<DateBucket>(value, out var bucket))
                {
                    if (Model is DateGroup d) d.Bucket = bucket;
                    else Model = new DateGroup { Variable = Model.Variable, Bucket = bucket };
                }
                else if (Model is DateGroup) Model = new ValueGroup { Variable = Model.Variable };
                OnPropertyChanged(); OnPropertyChanged(nameof(IsFiscal)); OnPropertyChanged(nameof(FiscalYearStartMonth));
            }
        }
        public int FiscalYearStartMonth
        {
            get => (Model as DateGroup)?.FiscalYearStartMonth ?? 1;
            set { if (Model is DateGroup d) d.FiscalYearStartMonth = value; OnPropertyChanged(); }
        }
        public bool IsDate => Owner.IsDateField(Variable);
        //年度の開始月が効くのは 年・四半期 だけ
        public bool IsFiscal => IsDate && Model is DateGroup { Bucket: DateBucket.Year or DateBucket.Quarter };
    }

    class MeasureItemViewModel : CrossTabItemViewModelBase
    {
        public MeasureItemViewModel(CrossTabSettingViewModel owner, AggregateMeasure model, Action remove)
        {
            Owner = owner;
            Model = model;
            RemoveCommand = new CrossTabRelayCommand(remove);
        }
        public CrossTabSettingViewModel Owner { get; }
        public AggregateMeasure Model { get; }
        public AggregateFunction Function
        {
            get => Model.Function;
            set { Model.Function = value; OnPropertyChanged(); OnPropertyChanged(nameof(NeedsVariable)); Owner.Refresh(); }
        }
        public string Variable
        {
            get => Model.Variable;
            set { Model.Variable = value ?? string.Empty; OnPropertyChanged(); Owner.Refresh(); }
        }
        public string Name
        {
            get => Model.Name;
            set { Model.Name = value ?? string.Empty; OnPropertyChanged(); Owner.Refresh(); }
        }
        //表示の書式 (.NET の数値の書式。空なら元の項目の書式)。件数には使わない
        public string Format
        {
            get => Model.Format;
            set { Model.Format = value ?? string.Empty; OnPropertyChanged(); }
        }
        public bool NeedsVariable => Model.Function != AggregateFunction.Count;
    }

    class HavingItemViewModel : CrossTabItemViewModelBase
    {
        public HavingItemViewModel(CrossTabSettingViewModel owner, AggregateHaving model, Action remove)
        {
            Owner = owner;
            Model = model;
            RemoveCommand = new CrossTabRelayCommand(remove);
        }
        public CrossTabSettingViewModel Owner { get; }
        public AggregateHaving Model { get; }
        public int MeasureIndex { get => Model.MeasureIndex; set { Model.MeasureIndex = value; OnPropertyChanged(); } }
        public MatchComparison Comparison { get => Model.Comparison; set { Model.Comparison = value; OnPropertyChanged(); } }
        public string Value
        {
            get => Model.Value.ToString(CultureInfo.InvariantCulture);
            set { if (decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) Model.Value = d; OnPropertyChanged(); }
        }
    }

    class SortItemViewModel : CrossTabItemViewModelBase
    {
        public SortItemViewModel(CrossTabSettingViewModel owner, AggregateSort model, Action remove)
        {
            Owner = owner;
            Model = model;
            RemoveCommand = new CrossTabRelayCommand(remove);
        }
        public CrossTabSettingViewModel Owner { get; }
        public AggregateSort Model { get; }
        public AggregateSortTarget Target
        {
            get => Model.Target;
            set { Model.Target = value; OnPropertyChanged(); OnPropertyChanged(nameof(IndexItems)); }
        }
        public int Index { get => Model.Index; set { Model.Index = value; OnPropertyChanged(); } }
        public bool IsDescending { get => Model.IsDescending; set { Model.IsDescending = value; OnPropertyChanged(); } }
        public IEnumerable<IndexItem> IndexItems => Target == AggregateSortTarget.Group ? Owner.GroupIndexItems : Owner.MeasureIndexItems;
        public void NotifyIndexItems() => OnPropertyChanged(nameof(IndexItems));
    }

    class CrossTabSettingViewModel : INotifyPropertyChanged
    {
        readonly DesignData _designData;
        readonly string _moduleName;
        readonly CrossTabSetting _value;

        public CrossTabSettingViewModel(DesignData designData, string moduleName, CrossTabSetting value)
        {
            _designData = designData;
            _moduleName = moduleName;
            _value = value;
            foreach (var g in value.Rows) Rows.Add(new GroupItemViewModel(this, g, () => Remove(Rows, g)));
            foreach (var g in value.Columns) Columns.Add(new GroupItemViewModel(this, g, () => Remove(Columns, g)));
            foreach (var m in value.Measures) Measures.Add(new MeasureItemViewModel(this, m, () => Remove(Measures, m)));
            foreach (var h in value.Having) Having.Add(new HavingItemViewModel(this, h, () => Remove(Having, h)));
            foreach (var s in value.SortConditions) Sorts.Add(new SortItemViewModel(this, s, () => Remove(Sorts, s)));
            AddRowCommand = new CrossTabRelayCommand(() => { var g = new ValueGroup(); Rows.Add(new GroupItemViewModel(this, g, () => Remove(Rows, g))); Refresh(); });
            AddColumnCommand = new CrossTabRelayCommand(() => { var g = new ValueGroup(); Columns.Add(new GroupItemViewModel(this, g, () => Remove(Columns, g))); Refresh(); });
            AddMeasureCommand = new CrossTabRelayCommand(() => { var m = new AggregateMeasure(); Measures.Add(new MeasureItemViewModel(this, m, () => Remove(Measures, m))); Refresh(); });
            AddHavingCommand = new CrossTabRelayCommand(() => { var h = new AggregateHaving { Comparison = MatchComparison.GreaterThanOrEqual }; Having.Add(new HavingItemViewModel(this, h, () => Remove(Having, h))); });
            AddSortCommand = new CrossTabRelayCommand(() => { var s = new AggregateSort(); Sorts.Add(new SortItemViewModel(this, s, () => Remove(Sorts, s))); });
            FieldCandidates = CreateFieldCandidates();
        }

        public ObservableCollection<GroupItemViewModel> Rows { get; } = [];
        public ObservableCollection<GroupItemViewModel> Columns { get; } = [];
        public ObservableCollection<MeasureItemViewModel> Measures { get; } = [];
        public ObservableCollection<HavingItemViewModel> Having { get; } = [];
        public ObservableCollection<SortItemViewModel> Sorts { get; } = [];
        public ICommand AddRowCommand { get; }
        public ICommand AddColumnCommand { get; }
        public ICommand AddMeasureCommand { get; }
        public ICommand AddHavingCommand { get; }
        public ICommand AddSortCommand { get; }

        public List<string> FieldCandidates { get; }
        //まとめる単位の候補: まとめない + 日付の単位 (表示は CrossTabSetting_Enum_* で日本語化)
        public IEnumerable<string> Buckets => new[] { GroupItemViewModel.NoneBucket }.Concat(Enum.GetNames<DateBucket>());
        //値の書式の候補 (自由入力も可)
        public IEnumerable<string> FormatCandidates => CrossTabFormatPresets.All.Select(e => e.Code);
        public IEnumerable<int> Months => Enumerable.Range(1, 12);
        public IEnumerable<AggregateFunction> Functions => Enum.GetValues<AggregateFunction>();
        public IEnumerable<MatchComparison> Comparisons =>
        [
            MatchComparison.Equal, MatchComparison.NotEqual, MatchComparison.LessThan, MatchComparison.LessThanOrEqual, MatchComparison.GreaterThan, MatchComparison.GreaterThanOrEqual,
        ];
        public IEnumerable<AggregateSortTarget> SortTargets => Enum.GetValues<AggregateSortTarget>();
        /// <summary>値の候補 "番号: 表示名 (無ければ 項目 集計方法)"。値が無いときは 1 件だけ。</summary>
        public IEnumerable<IndexItem> MeasureIndexItems
            => Measures.Count == 0 ? [new IndexItem(0, "1")] : Measures.Select((m, i) => new IndexItem(i, $"{i + 1}: {MeasureText(m)}"));

        /// <summary>行・列の候補 "番号: 項目" (行 → 列の順)。</summary>
        public IEnumerable<IndexItem> GroupIndexItems
        {
            get
            {
                var groups = Rows.Concat(Columns).ToList();
                if (groups.Count == 0) return [new IndexItem(0, "1")];
                return groups.Select((g, i) => new IndexItem(i, $"{i + 1}: {(string.IsNullOrEmpty(g.Variable) ? "-" : g.Variable)}"));
            }
        }

        static string MeasureText(MeasureItemViewModel m)
        {
            if (!string.IsNullOrEmpty(m.Name)) return m.Name;
            var function = Properties.Resources.ResourceManager.GetString("CrossTabSetting_Enum_" + m.Function, Properties.Resources.Culture) ?? m.Function.ToString();
            return m.Function == AggregateFunction.Count || string.IsNullOrEmpty(m.Variable) ? function : $"{m.Variable} {function}";
        }

        public string LimitCount
        {
            get => _value.LimitCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            set { _value.LimitCount = int.TryParse(value, out var n) && n > 0 ? n : null; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        /// <summary>行・列・値が変わったら、番号で指す候補 (値で絞り込み・並べ替え) を作り直す。</summary>
        public void Refresh()
        {
            OnPropertyChanged(nameof(MeasureIndexItems));
            OnPropertyChanged(nameof(GroupIndexItems));
            foreach (var s in Sorts) s.NotifyIndexItems();
        }

        //値・行・列を消したら、それを指す値で絞り込み・並べ替えを外し、後ろの番号を詰める (実行時のエディタと同じ。番号は 行 → 列 の通し)
        void Remove<TVm, TModel>(ObservableCollection<TVm> items, TModel model) where TVm : CrossTabItemViewModelBase
        {
            var item = items.FirstOrDefault(e => ReferenceEquals(ModelOf(e), model));
            if (item == null) return;
            var groupIndex = item is GroupItemViewModel g ? Rows.Concat(Columns).ToList().IndexOf(g) : -1;
            var measureIndex = item is MeasureItemViewModel m ? Measures.IndexOf(m) : -1;
            items.Remove(item);
            if (0 <= measureIndex)
            {
                foreach (var h in Having.Where(h => h.MeasureIndex == measureIndex).ToList()) Having.Remove(h);
                foreach (var h in Having.Where(h => measureIndex < h.MeasureIndex)) h.MeasureIndex--;
                RemoveSortIndex(AggregateSortTarget.Measure, measureIndex);
            }
            if (0 <= groupIndex) RemoveSortIndex(AggregateSortTarget.Group, groupIndex);
            Refresh();
        }

        void RemoveSortIndex(AggregateSortTarget target, int index)
        {
            foreach (var s in Sorts.Where(s => s.Target == target && s.Index == index).ToList()) Sorts.Remove(s);
            foreach (var s in Sorts.Where(s => s.Target == target && index < s.Index)) s.Index--;
        }

        static object ModelOf(CrossTabItemViewModelBase vm) => vm switch
        {
            GroupItemViewModel g => g.Model,
            MeasureItemViewModel m => m.Model,
            HavingItemViewModel h => h.Model,
            SortItemViewModel s => s.Model,
            _ => vm,
        };

        /// <summary>画面の並びを値に書き戻す (削除・追加を反映)。</summary>
        public void Apply()
        {
            _value.Rows = Rows.Select(e => e.Model).ToList();
            _value.Columns = Columns.Select(e => e.Model).ToList();
            _value.Measures = Measures.Select(e => e.Model).ToList();
            _value.Having = Having.Select(e => e.Model).ToList();
            _value.SortConditions = Sorts.Select(e => e.Model).ToList();
        }

        public bool IsDateField(string variable)
            => Resolve(variable) is DateFieldDesign or DateTimeFieldDesign;

        FieldDesignBase? Resolve(string variable)
        {
            var module = _designData.Modules.Find(_moduleName);
            return module == null ? null : CrossTabFieldDesign.ResolveField(_designData, module, variable);
        }

        //候補: 自モジュールの値項目と、リンク先 1 段の値項目
        List<string> CreateFieldCandidates()
        {
            var list = new List<string>();
            var module = _designData.Modules.Find(_moduleName);
            if (module == null) return list;
            foreach (var field in module.Fields.Where(IsScalar))
            {
                list.Add(field.Name + ".Value");
                var target = CrossTabFieldDesign.LinkTargetModuleName(field);
                if (string.IsNullOrEmpty(target)) continue;
                var targetModule = _designData.Modules.Find(target);
                if (targetModule == null) continue;
                list.AddRange(targetModule.Fields.Where(IsScalar).Select(f => $"{field.Name}.{f.Name}.Value"));
            }
            return list;
        }

        static bool IsScalar(FieldDesignBase field)
            => field is DbValueFieldDesignBase && field is not IListFieldDesign && field.Name.IndexOf('.') < 0;
    }
}
