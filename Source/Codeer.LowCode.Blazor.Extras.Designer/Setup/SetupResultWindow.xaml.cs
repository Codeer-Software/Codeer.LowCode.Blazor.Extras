using Codeer.LowCode.Blazor.Designer;
using Codeer.LowCode.Blazor.Designer.Extensibility;
using Codeer.LowCode.Blazor.Designer.Views.Windows;
using Codeer.LowCode.Blazor.SystemSettings;
using MahApps.Metro.Controls;
using Microsoft.Extensions.DependencyInjection;
using System.Data;
using System.Text;
using System.Windows;

namespace Codeer.LowCode.Blazor.Extras.Designer.Setup
{
    /// <summary>
    /// セットアップ結果の表示。生成/スキップの内訳と DDL を示し、その場で DDL を実行できる
    /// (実行はユーザーのボタン操作 = 確認を挟む。Standard の DDLWindow と同じ流儀)。
    /// </summary>
    public partial class SetupResultWindow : MetroWindow
    {
        DesignerEnvironment? _environment;
        DataSource? _dataSource;

        SetupResultWindow(SetupResult result)
        {
            InitializeComponent();

            Title = Properties.Resources.SetupResultTitle;
            _labelDdl.Text = Properties.Resources.SetupDdlHeader;
            _buttonRun.Content = Properties.Resources.SetupRun;
            _buttonCopy.Content = Properties.Resources.SetupCopy;
            _buttonClose.Content = Properties.Resources.SetupClose;

            var summary = new StringBuilder();
            if (result.CreatedModules.Count > 0)
                summary.AppendLine(string.Format(Properties.Resources.SetupCreatedFormat, string.Join(", ", result.CreatedModules)));
            if (result.SkippedModules.Count > 0)
                summary.AppendLine(string.Format(Properties.Resources.SetupSkippedFormat, string.Join(", ", result.SkippedModules)));
            foreach (var note in result.Notes) summary.AppendLine(note);
            if (result.CreatedModules.Count > 0)
                summary.AppendLine(Properties.Resources.SetupReloadNote);
            _textSummary.Text = summary.ToString().TrimEnd();

            _textDdl.Text = string.Join(Environment.NewLine, result.Ddl);
            if (result.Ddl.Count == 0)
            {
                _labelDdl.Visibility = Visibility.Collapsed;
                _textDdl.Visibility = Visibility.Collapsed;
                _buttonRun.Visibility = Visibility.Collapsed;
                _buttonCopy.Visibility = Visibility.Collapsed;
            }
        }

        internal static void ShowResult(DesignerEnvironment environment, DataSource dataSource, SetupResult result)
        {
            new SetupResultWindow(result)
            {
                _environment = environment,
                _dataSource = dataSource,
                Owner = Application.Current.MainWindow,
            }.ShowDialog();
        }

        async void RunClick(object sender, RoutedEventArgs e)
        {
            if (_environment == null || _dataSource == null) return;
            try
            {
                await using var dbAccess = _environment.ServiceProvider
                    .GetRequiredService<IDbAccessorFactory>().Create([_dataSource]);

                var conn = dbAccess.GetConnection(_dataSource.Name);
                using var cmd = conn.CreateCommand();
                cmd.CommandText = _textDdl.Text;
                cmd.CommandType = CommandType.Text;
                await cmd.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                MessageWindow.Show(ex.Message);
                return;
            }
            _environment.RefreshDatabase();
            _environment.ShowToast(Properties.Resources.SetupDdlDone, true);
        }

        void CopyClick(object sender, RoutedEventArgs e)
            => Clipboard.SetText(_textDdl.Text);
    }
}
