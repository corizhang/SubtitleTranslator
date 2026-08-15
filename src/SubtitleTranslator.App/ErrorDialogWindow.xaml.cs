using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SubtitleTranslator.App;

public partial class ErrorDialogWindow : Window
{
    private ErrorDialogWindow(Exception exception)
    {
        InitializeComponent();
        DetailsBox.Text = $"{exception.GetType().Name}: {exception.Message}\n\n日志：{AppFileLogger.CurrentLogPath}";
    }
    public static void ShowFatal(Exception exception)
    {
        try { new ErrorDialogWindow(exception).ShowDialog(); }
        catch { MessageBox.Show(exception.Message, "AI 字幕翻译发生错误", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void Copy_OnClick(object sender, RoutedEventArgs e) => Clipboard.SetText(DetailsBox.Text);
    private void OpenLogs_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppFileLogger.LogDirectory);
        Process.Start(new ProcessStartInfo(AppFileLogger.LogDirectory) { UseShellExecute = true });
    }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
