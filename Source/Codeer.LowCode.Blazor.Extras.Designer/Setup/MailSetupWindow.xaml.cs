using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>メールのセットアップのオプション入力ダイアログ。</summary>
    public partial class MailSetupWindow : MetroWindow
    {
        MailSetupOptions? _result;

        MailSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuMail;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserEmailField.Text = Properties.Resources.SetupUserEmailField;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;
            _checkSenderContract.Content = Properties.Resources.SetupSenderContract;
            _checkGmailToken.Content = Properties.Resources.SetupGmailToken;
            _checkHistory.Content = Properties.Resources.SetupMailHistory;
            _labelHistoryName.Text = Properties.Resources.SetupModuleName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;
            _labelInfra.Text = Properties.Resources.SetupMailInfra;

            var moduleNames = designData.Modules.GetModuleNames();
            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName;
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();

            //既に差出人契約があれば、その宣言をユーザー項目の既定値にする
            var (emailField, displayNameField) = MailSetupService.ReadSenderRoles(designData.Modules.Find(userModule));
            _textUserEmailField.Text = emailField ?? "Email";
            _textUserNameField.Text = displayNameField ?? "Name";

            _textHistoryName.Text = "MailHistory";
            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            foreach (var name in MailSetupOptions.InfraNames) _comboInfra.Items.Add(name);
            _comboInfra.SelectedIndex = 0;

            _checkHistory.Checked += (_, _) => SetHistoryEnabled(true);
            _checkHistory.Unchecked += (_, _) => SetHistoryEnabled(false);
        }

        void SetHistoryEnabled(bool enabled)
        {
            _textHistoryName.IsEnabled = enabled;
            _comboDataSource.IsEnabled = enabled;
            _checkPageFrame.IsEnabled = enabled;
        }

        internal static MailSetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new MailSetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (_comboUserModule.SelectedItem == null) return;
            if (_checkHistory.IsChecked == true &&
                (_comboDataSource.SelectedItem == null || string.IsNullOrWhiteSpace(_textHistoryName.Text))) return;

            _result = new MailSetupOptions
            {
                UserModuleName = (string)_comboUserModule.SelectedItem,
                UserEmailField = string.IsNullOrWhiteSpace(_textUserEmailField.Text) ? "Email" : _textUserEmailField.Text.Trim(),
                UserDisplayNameField = string.IsNullOrWhiteSpace(_textUserNameField.Text) ? "Name" : _textUserNameField.Text.Trim(),
                AddSenderContract = _checkSenderContract.IsChecked == true,
                AddGmailTokenField = _checkGmailToken.IsChecked == true,
                CreateHistoryModule = _checkHistory.IsChecked == true,
                HistoryModuleName = _textHistoryName.Text.Trim(),
                DataSourceName = (string?)_comboDataSource.SelectedItem ?? string.Empty,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
                DefaultInfraName = (string)_comboInfra.SelectedItem,
            };
            Close();
        }
    }
}
