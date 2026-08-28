using MailSender.Services;
using System.Windows;

namespace MailSender
{
    public partial class SettingsWindow : Window
    {
        readonly AppSettings _settings;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            _clientId.Text = settings.ClientId;
            _clientId.Focus();
        }

        void OnOk(object sender, RoutedEventArgs e)
        {
            _settings.ClientId = _clientId.Text.Trim();
            _settings.Save();
            DialogResult = true;
        }
    }
}
