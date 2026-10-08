using Codeer.LowCode.Blazor.DesignLogic;
using MahApps.Metro.Controls;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>タグのセットアップのオプション入力ダイアログ (タグを付けるモジュールを選ぶだけ)。</summary>
    public partial class TagSetupWindow : MetroWindow
    {
        TagSetupOptions? _result;

        TagSetupWindow(DesignData designData)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupMenuTag;
            _labelTarget.Text = Properties.Resources.SetupTagTarget;

            //タグを付けられるのはテーブルを持つモジュール
            foreach (var name in designData.Modules.GetModuleNames().Where(e => !string.IsNullOrEmpty(designData.Modules.Find(e)?.DbTable)))
                _comboTarget.Items.Add(name);
            if (_comboTarget.Items.Count > 0) _comboTarget.SelectedIndex = 0;
        }

        internal static TagSetupOptions? ShowDialog(DesignData designData)
        {
            var window = new TagSetupWindow(designData)
            {
                Owner = Application.Current.MainWindow,
            };
            window.ShowDialog();
            return window._result;
        }

        void OkClick(object sender, RoutedEventArgs e)
        {
            if (_comboTarget.SelectedItem == null) return;

            _result = new TagSetupOptions
            {
                TargetModuleName = (string)_comboTarget.SelectedItem,
            };
            Close();
        }
    }
}
