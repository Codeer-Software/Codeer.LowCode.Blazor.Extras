using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>承認フローセットアップのオプション入力ダイアログ。</summary>
    public partial class ApprovalSetupWindow : MetroWindow
    {
        ApprovalSetupOptions? _result;

        ApprovalSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuApprovalFlow;
            _labelTarget.Text = Properties.Resources.SetupTargetModule;
            _labelFieldName.Text = Properties.Resources.SetupFieldName;
            _labelDbColumn.Text = Properties.Resources.SetupDbColumn;
            _labelPrefix.Text = Properties.Resources.SetupPrefix;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _labelUserModule.Text = Properties.Resources.SetupUserModule;
            _labelUserNameField.Text = Properties.Resources.SetupUserNameField;
            _labelUserEmailField.Text = Properties.Resources.SetupUserEmailField;
            _labelRouteMaster.Text = Properties.Resources.SetupRouteMaster;
            _checkTurnMail.Content = Properties.Resources.SetupTurnMail;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            var moduleNames = designData.Modules.GetModuleNames();

            _comboTarget.Items.Add(Properties.Resources.SetupTargetNone);
            foreach (var name in moduleNames) _comboTarget.Items.Add(name);
            _comboTarget.SelectedIndex = 0;

            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;

            foreach (var name in moduleNames) _comboUserModule.Items.Add(name);
            var userModule = string.IsNullOrEmpty(designData.AppSettings.CurrentUserModuleDesignName)
                ? "AppUser" : designData.AppSettings.CurrentUserModuleDesignName;
            _comboUserModule.SelectedItem = moduleNames.Contains(userModule) ? userModule : moduleNames.FirstOrDefault();

            _comboRouteMaster.Items.Add(Properties.Resources.SetupRouteStandard);
            _comboRouteMaster.Items.Add(Properties.Resources.SetupRouteSimple);
            _comboRouteMaster.Items.Add(Properties.Resources.SetupRouteNone);
            _comboRouteMaster.SelectedIndex = 0;

            _textFieldName.Text = "Approval";
            _textDbColumn.Text = "approval_id";
            _textUserNameField.Text = "Name";
            _textUserEmailField.Text = "Email";
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
            if (string.IsNullOrWhiteSpace(_textFieldName.Text) || string.IsNullOrWhiteSpace(_textDbColumn.Text)) return;

            _result = new ApprovalSetupOptions
            {
                TargetModuleName = _comboTarget.SelectedIndex <= 0 ? string.Empty : (string)_comboTarget.SelectedItem,
                FieldName = _textFieldName.Text.Trim(),
                DbColumn = _textDbColumn.Text.Trim(),
                Prefix = _textPrefix.Text.Trim(),
                DataSourceName = (string)_comboDataSource.SelectedItem,
                UserModuleName = (string)_comboUserModule.SelectedItem,
                UserDisplayNameField = string.IsNullOrWhiteSpace(_textUserNameField.Text) ? "Name" : _textUserNameField.Text.Trim(),
                UserEmailField = string.IsNullOrWhiteSpace(_textUserEmailField.Text) ? "Email" : _textUserEmailField.Text.Trim(),
                RouteMaster = _comboRouteMaster.SelectedIndex switch
                {
                    1 => ApprovalRouteMasterKind.Simple,
                    2 => ApprovalRouteMasterKind.None,
                    _ => ApprovalRouteMasterKind.Standard,
                },
                UseTurnNotifyMail = _checkTurnMail.IsChecked == true,
                AddPageFrameLinks = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
