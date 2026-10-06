using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>タグのセットアップのオプション入力ダイアログ。</summary>
    public partial class TagSetupWindow : MetroWindow
    {
        TagSetupOptions? _result;
        string _autoLinkName = string.Empty;

        TagSetupWindow(DesignData designData, List<string> dataSourceNames)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuTag;
            _labelTarget.Text = Properties.Resources.SetupTagTarget;
            _labelFieldName.Text = Properties.Resources.SetupTagFieldName;
            _labelLinkModule.Text = Properties.Resources.SetupTagLinkModule;
            _labelMasterModule.Text = Properties.Resources.SetupTagMasterModule;
            _labelDataSource.Text = Properties.Resources.SetupDataSource;
            _checkPageFrame.Content = Properties.Resources.SetupPageFrame;

            _textFieldName.Text = "Tags";
            _textMasterModule.Text = "Tag";

            //タグを付けられるのはテーブルを持つモジュール
            foreach (var name in designData.Modules.GetModuleNames().Where(e => !string.IsNullOrEmpty(designData.Modules.Find(e)?.DbTable)))
                _comboTarget.Items.Add(name);
            //タグ付けモジュール名は対象 + Tags (手で変えたらそのまま)
            _comboTarget.SelectionChanged += (_, _) =>
            {
                var next = (string?)_comboTarget.SelectedItem + "Tags";
                if (string.IsNullOrEmpty(_textLinkModule.Text) || _textLinkModule.Text == _autoLinkName) _textLinkModule.Text = next;
                _autoLinkName = next;
            };
            if (_comboTarget.Items.Count > 0) _comboTarget.SelectedIndex = 0;

            foreach (var name in dataSourceNames) _comboDataSource.Items.Add(name);
            if (_comboDataSource.Items.Count > 0) _comboDataSource.SelectedIndex = 0;
        }

        internal static TagSetupOptions? ShowDialog(DesignData designData, List<string> dataSourceNames)
        {
            var window = new TagSetupWindow(designData, dataSourceNames)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (_comboTarget.SelectedItem == null || _comboDataSource.SelectedItem == null) return;
            if (string.IsNullOrWhiteSpace(_textLinkModule.Text) || string.IsNullOrWhiteSpace(_textMasterModule.Text)) return;

            _result = new TagSetupOptions
            {
                TargetModuleName = (string)_comboTarget.SelectedItem,
                FieldName = _textFieldName.Text.Trim(),
                LinkModuleName = _textLinkModule.Text.Trim(),
                MasterModuleName = _textMasterModule.Text.Trim(),
                DataSourceName = (string)_comboDataSource.SelectedItem,
                AddPageFrameLink = _checkPageFrame.IsChecked == true,
            };
            Close();
        }
    }
}
