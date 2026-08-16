using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>メール履歴モジュール生成のオプション入力ダイアログ。</summary>
    public partial class MailHistorySetupWindow : MetroWindow
    {
        MailHistorySetupOptions? _result;
        readonly string _userModuleName;

        MailHistorySetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuMailHistory;
            _labelModuleName.Text = Properties.Resources.SetupModuleName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            _textModuleName.Text = "MailHistory";
            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            _userModuleName = string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName;
        }

        internal static MailHistorySetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new MailHistorySetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (_comboDataSource.SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(_textModuleName.Text)) return;

            _result = new MailHistorySetupOptions
            {
                ModuleName = _textModuleName.Text.Trim(),
                DataSourceName = (string)_comboDataSource.SelectedItem,
                UserModuleName = _userModuleName,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
