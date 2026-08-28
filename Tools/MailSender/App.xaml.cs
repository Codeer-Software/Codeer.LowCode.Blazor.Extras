using System.Windows;

namespace MailSender
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            //第 1 引数 = プレビュー HTML のパス (ダブルクリック / 「プログラムから開く」)
            var window = new MainWindow(e.Args.Length > 0 ? e.Args[0] : null);
            window.Show();
        }
    }
}
