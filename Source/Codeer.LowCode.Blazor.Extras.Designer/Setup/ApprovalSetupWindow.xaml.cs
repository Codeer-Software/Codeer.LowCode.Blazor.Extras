using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>承認フローセットアップのオプション入力ダイアログ。</summary>
    public partial class ApprovalSetupWindow : MetroWindow
    {
        readonly DesignData _designData;
        ApprovalSetupOptions? _result;

        ApprovalSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();
            _designData = designData;

            Title = Properties.Resources.SetupMenuApprovalFlow;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;
            _labelUserEmailField.Text = Properties.Resources.SetupUserEmailField;
            _labelRouteMaster.Text = Properties.Resources.SetupRouteMaster;
            _checkTurnMail.Content = Properties.Resources.SetupTurnMail;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            var moduleNames = designData.Modules.GetModuleNames();

            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = SetupUi.CurrentUserModuleName(designData);
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();
            _comboUserModule.SelectionChanged += (_, _) => FillUserFields();
            FillUserFields();

            _comboRouteMaster.Items.Add(Properties.Resources.SetupRouteStandard);
            _comboRouteMaster.Items.Add(Properties.Resources.SetupRouteNone);
            _comboRouteMaster.SelectedIndex = 0;

            //メールアドレスは通知メールを含めるときだけ使う
            _checkTurnMail.Checked += (_, _) => _comboUserEmailField.IsEnabled = true;
            _checkTurnMail.Unchecked += (_, _) => _comboUserEmailField.IsEnabled = false;
        }

        //選んだユーザーモジュールのフィールドを候補にする。既に差出人契約があれば、その宣言を初期選択にする
        void FillUserFields()
        {
            var module = _designData.Modules.Find((string?)_comboUserModule.SelectedItem ?? string.Empty);
            var (emailField, displayNameField) = MailSetupService.ReadSenderRoles(module);
            SetupUi.FillFields(_comboUserNameField, module, displayNameField, "Name");
            SetupUi.FillFields(_comboUserEmailField, module, emailField, "Email");
        }

        internal static ApprovalSetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new ApprovalSetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (_comboDataSource.SelectedItem == null || _comboUserModule.SelectedItem == null) return;
            if (_comboUserNameField.SelectedItem == null) return;
            if (_checkTurnMail.IsChecked == true && _comboUserEmailField.SelectedItem == null) return;

            _result = new ApprovalSetupOptions
            {
                DataSourceName = (string)_comboDataSource.SelectedItem,
                UserModuleName = (string)_comboUserModule.SelectedItem,
                UserDisplayNameField = (string)_comboUserNameField.SelectedItem,
                UserEmailField = (string?)_comboUserEmailField.SelectedItem ?? "Email",
                RouteMaster = _comboRouteMaster.SelectedIndex == 1
                    ? ApprovalRouteMasterKind.None : ApprovalRouteMasterKind.Standard,
                UseTurnNotifyMail = _checkTurnMail.IsChecked == true,
                AddPageFrameLinks = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
