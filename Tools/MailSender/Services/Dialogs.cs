using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;

namespace MailSender.Services
{
    /// <summary>
    /// アプリ内のメッセージボックス (WPF-UI の Fluent スタイル)。System.Windows.MessageBox は使わない (見た目が古い Win32 のダイアログになる)。
    /// 情報 / 警告 / エラーは「OK」だけ、確認は「OK / キャンセル」(破壊的な操作は OK を赤に)。
    /// </summary>
    public static class Dialogs
    {
        public const string AppTitle = "MailSender";

        enum Kind { Information, Warning, Error, Question }

        public static Task InfoAsync(Window owner, string message, string title = AppTitle)
            => ShowAsync(owner, title, message, Kind.Information, okText: "OK", cancelText: null, danger: false);

        public static Task WarningAsync(Window owner, string message, string title = AppTitle)
            => ShowAsync(owner, title, message, Kind.Warning, okText: "OK", cancelText: null, danger: false);

        public static Task ErrorAsync(Window owner, string message, string title)
            => ShowAsync(owner, title, message, Kind.Error, okText: "OK", cancelText: null, danger: false);

        /// <summary>OK / キャンセル。<paramref name="danger"/> = 取り消せない操作 (破棄など) は OK ボタンを赤にする。</summary>
        public static async Task<bool> ConfirmAsync(Window owner, string message, string title = AppTitle, string okText = "OK", bool danger = false, bool warning = false)
            => await ShowAsync(owner, title, message, warning ? Kind.Warning : Kind.Question, okText, "キャンセル", danger) == Wpf.Ui.Controls.MessageBoxResult.Primary;

        static async Task<Wpf.Ui.Controls.MessageBoxResult> ShowAsync(Window owner, string title, string message, Kind kind, string okText, string? cancelText, bool danger)
        {
            var (symbol, brushKey) = kind switch
            {
                Kind.Warning => (SymbolRegular.Warning28, "SystemFillColorCautionBrush"),
                Kind.Error => (SymbolRegular.ErrorCircle24, "SystemFillColorCriticalBrush"),
                Kind.Question => (SymbolRegular.QuestionCircle24, "AccentTextFillColorPrimaryBrush"),
                _ => (SymbolRegular.Info28, "AccentTextFillColorPrimaryBrush"),
            };
            var icon = new SymbolIcon(symbol) { FontSize = 30, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 14, 0) };
            if (Application.Current.TryFindResource(brushKey) is Brush brush) icon.Foreground = brush;

            var content = new StackPanel { Orientation = Orientation.Horizontal, MaxWidth = 520 };
            content.Children.Add(icon);
            content.Children.Add(new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 460,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var box = new Wpf.Ui.Controls.MessageBox
            {
                Owner = owner,
                Title = title,
                Content = content,
                MinWidth = 380,
                MaxWidth = 600,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
            };
            if (cancelText == null)
            {
                //OK だけ: 閉じるボタンを OK にする (プライマリは非表示)
                box.IsPrimaryButtonEnabled = false;
                box.CloseButtonText = okText;
                box.CloseButtonAppearance = ControlAppearance.Primary;
            }
            else
            {
                box.PrimaryButtonText = okText;
                box.PrimaryButtonAppearance = danger ? ControlAppearance.Danger : ControlAppearance.Primary;
                box.CloseButtonText = cancelText;
            }
            return await box.ShowDialogAsync();
        }
    }
}
