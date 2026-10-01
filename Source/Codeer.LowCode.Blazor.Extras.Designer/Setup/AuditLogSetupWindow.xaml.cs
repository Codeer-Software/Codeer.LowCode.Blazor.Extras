using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>監査ログのセットアップのオプション入力ダイアログ。</summary>
    public partial class AuditLogSetupWindow : MetroWindow
    {
        readonly DesignData _designData;
        AuditLogSetupOptions? _result;

        AuditLogSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();
            _designData = designData;

            Title = Properties.Resources.SetupMenuAuditLog;
            _labelModuleName.Text = Properties.Resources.SetupModuleName;
            _labelTableName.Text = Properties.Resources.SetupTableName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            var defaults = new AuditLogSetupOptions();
            _textModuleName.Text = defaults.ModuleName;
            _textTableName.Text = defaults.TableName;

            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            var moduleNames = designData.Modules.GetModuleNames();
            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = SetupUi.CurrentUserModuleName(designData);
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();
            _comboUserModule.SelectionChanged += (_, _) => FillUserFields();
            FillUserFields();
        }

        //選んだユーザーモジュールのフィールドを候補にする (ユーザーリンクの表示名)
        void FillUserFields()
        {
            var module = _designData.Modules.Find((string?)_comboUserModule.SelectedItem ?? string.Empty);
            SetupUi.FillFields(_comboUserNameField, module, null, "Name");
        }

        internal static AuditLogSetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new AuditLogSetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_textModuleName.Text) || string.IsNullOrWhiteSpace(_textTableName.Text)) return;
            if (_comboDataSource.SelectedItem == null || _comboUserModule.SelectedItem == null) return;
            if (_comboUserNameField.SelectedItem == null) return;

            _result = new AuditLogSetupOptions
            {
                ModuleName = _textModuleName.Text.Trim(),
                TableName = _textTableName.Text.Trim(),
                DataSourceName = (string)_comboDataSource.SelectedItem,
                UserModuleName = (string)_comboUserModule.SelectedItem,
                UserDisplayNameField = (string)_comboUserNameField.SelectedItem,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
