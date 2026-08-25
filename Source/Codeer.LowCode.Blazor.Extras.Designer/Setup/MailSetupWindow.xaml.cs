using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>メールのセットアップのオプション入力ダイアログ。</summary>
    public partial class MailSetupWindow : MetroWindow
    {
        readonly DesignData _designData;
        MailSetupOptions? _result;

        MailSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();
            _designData = designData;

            Title = Properties.Resources.SetupMenuMail;
            _checkHistory.Content = Properties.Resources.SetupMailHistory;
            _labelHistoryName.Text = Properties.Resources.SetupModuleName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;
            _groupUser.Header = Properties.Resources.SetupUserGroup;
            _textUserHelp.Text = Properties.Resources.SetupUserGroupHelp;
            _checkSenderContract.Content = Properties.Resources.SetupSenderContract;
            _checkGmailToken.Content = Properties.Resources.SetupGmailToken;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserEmailField.Text = Properties.Resources.SetupUserEmailField;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;

            //送信履歴
            _textHistoryName.Text = "MailHistory";
            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;
            _checkHistory.Checked += (_, _) => UpdateEnabled();
            _checkHistory.Unchecked += (_, _) => UpdateEnabled();

            //ユーザーモジュール (差出人 = 操作ユーザーを使う場合だけ)
            var moduleNames = designData.Modules.GetModuleNames();
            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = SetupUi.CurrentUserModuleName(designData);
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();
            _comboUserModule.SelectionChanged += (_, _) => FillUserFields();
            FillUserFields();
            _checkSenderContract.Checked += (_, _) => UpdateEnabled();
            _checkSenderContract.Unchecked += (_, _) => UpdateEnabled();
            _checkGmailToken.Checked += (_, _) => UpdateEnabled();
            _checkGmailToken.Unchecked += (_, _) => UpdateEnabled();
            UpdateEnabled();
        }

        //選んだユーザーモジュールのフィールドを候補にする。既に差出人契約があればその宣言を初期選択にする
        void FillUserFields()
        {
            var module = _designData.Modules.Find((string?)_comboUserModule.SelectedItem ?? string.Empty);
            var (emailField, displayNameField) = MailSetupService.ReadSenderRoles(module);
            SetupUi.FillFields(_comboUserEmailField, module, emailField, "Email");
            SetupUi.FillFields(_comboUserNameField, module, displayNameField, "Name");
        }

        void UpdateEnabled()
        {
            var history = _checkHistory.IsChecked == true;
            _textHistoryName.IsEnabled = history;
            _comboDataSource.IsEnabled = history;
            _checkPageFrame.IsEnabled = history;

            var user = _checkSenderContract.IsChecked == true || _checkGmailToken.IsChecked == true;
            _comboUserModule.IsEnabled = user;
            _comboUserEmailField.IsEnabled = _checkSenderContract.IsChecked == true;
            _comboUserNameField.IsEnabled = _checkSenderContract.IsChecked == true;
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
            if (_checkHistory.IsChecked == true &&
                (_comboDataSource.SelectedItem == null || string.IsNullOrWhiteSpace(_textHistoryName.Text))) return;
            var useUser = _checkSenderContract.IsChecked == true || _checkGmailToken.IsChecked == true;
            if (useUser && _comboUserModule.SelectedItem == null) return;
            if (_checkSenderContract.IsChecked == true && _comboUserEmailField.SelectedItem == null) return;

            _result = new MailSetupOptions
            {
                CreateHistoryModule = _checkHistory.IsChecked == true,
                HistoryModuleName = _textHistoryName.Text.Trim(),
                DataSourceName = (string?)_comboDataSource.SelectedItem ?? string.Empty,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
                AddSenderContract = _checkSenderContract.IsChecked == true,
                AddGmailTokenField = _checkGmailToken.IsChecked == true,
                UserModuleName = (string?)_comboUserModule.SelectedItem ?? SetupUi.CurrentUserModuleName(_designData),
                UserEmailField = (string?)_comboUserEmailField.SelectedItem ?? "Email",
                UserDisplayNameField = (string?)_comboUserNameField.SelectedItem ?? "Name",
            };
            Close();
        }
    }
}
