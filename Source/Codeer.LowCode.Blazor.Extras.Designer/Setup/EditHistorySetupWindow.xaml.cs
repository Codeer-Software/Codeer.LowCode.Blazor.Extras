using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>編集履歴のセットアップのオプション入力ダイアログ。</summary>
    public partial class EditHistorySetupWindow : MetroWindow
    {
        readonly DesignData _designData;
        EditHistorySetupOptions? _result;

        EditHistorySetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();
            _designData = designData;

            Title = Properties.Resources.SetupMenuEditHistory;
            _labelModuleName.Text = Properties.Resources.SetupModuleName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;
            _checkTargetEnum.Content = Properties.Resources.SetupEditHistoryTargetEnum;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            _textModuleName.Text = "EditHistory";

            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            var moduleNames = designData.Modules.GetModuleNames();
            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = SetupUi.CurrentUserModuleName(designData);
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();
            _comboUserModule.SelectionChanged += (_, _) => FillUserFields();
            FillUserFields();
        }

        //選んだユーザーモジュールのフィールドを候補にする (変更者リンクの表示名)
        void FillUserFields()
        {
            var module = _designData.Modules.Find((string?)_comboUserModule.SelectedItem ?? string.Empty);
            SetupUi.FillFields(_comboUserNameField, module, null, UserModuleFields.DefaultDisplayNameField(module));
        }

        internal static EditHistorySetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new EditHistorySetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_textModuleName.Text)) return;
            if (_comboDataSource.SelectedItem == null || _comboUserModule.SelectedItem == null) return;
            if (_comboUserNameField.SelectedItem == null) return;

            _result = new EditHistorySetupOptions
            {
                HistoryModuleName = _textModuleName.Text.Trim(),
                DataSourceName = (string)_comboDataSource.SelectedItem,
                UserModuleName = (string)_comboUserModule.SelectedItem,
                UserDisplayNameField = (string)_comboUserNameField.SelectedItem,
                CreateTargetModuleEnum = _checkTargetEnum.IsChecked == true,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
