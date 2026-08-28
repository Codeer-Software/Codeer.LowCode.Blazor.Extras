using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>メールのセットアップのオプション入力ダイアログ。</summary>
    public partial class MailSetupWindow : MetroWindow
    {
        MailSetupOptions? _result;

        MailSetupWindow(List<string> dataSourceNames)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuMail;
            _checkHistory.Content = Properties.Resources.SetupMailHistory;
            _labelHistoryName.Text = Properties.Resources.SetupModuleName;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _checkHistoryDetail.Content = Properties.Resources.SetupMailHistoryDetail;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            //送信履歴
            _textHistoryName.Text = "MailHistory";
            _textHistoryDetailName.Text = "MailHistoryDetail";
            _checkHistoryDetail.Checked += (_, _) => UpdateEnabled();
            _checkHistoryDetail.Unchecked += (_, _) => UpdateEnabled();
            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;
            _checkHistory.Checked += (_, _) => UpdateEnabled();
            _checkHistory.Unchecked += (_, _) => UpdateEnabled();
            UpdateEnabled();
        }

        void UpdateEnabled()
        {
            var history = _checkHistory.IsChecked == true;
            _textHistoryName.IsEnabled = history;
            _comboDataSource.IsEnabled = history;
            _checkHistoryDetail.IsEnabled = history;
            _textHistoryDetailName.IsEnabled = history && _checkHistoryDetail.IsChecked == true;
            _checkPageFrame.IsEnabled = history;
        }

        internal static MailSetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new MailSetupWindow(dataSourceNames)
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

            _result = new MailSetupOptions
            {
                CreateHistoryModule = _checkHistory.IsChecked == true,
                HistoryModuleName = _textHistoryName.Text.Trim(),
                CreateHistoryDetailModule = _checkHistoryDetail.IsChecked == true && !string.IsNullOrWhiteSpace(_textHistoryDetailName.Text),
                HistoryDetailModuleName = _textHistoryDetailName.Text.Trim(),
                DataSourceName = (string?)_comboDataSource.SelectedItem ?? string.Empty,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
