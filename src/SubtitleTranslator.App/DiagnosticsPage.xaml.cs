using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SubtitleTranslator.App;

public partial class DiagnosticsPage : UserControl
{
    private readonly MainWindowViewModel viewModel;
    private bool loaded;
    public DiagnosticsPage(MainWindowViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel; }
    private async void Page_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!loaded) loaded = true;
        await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        await viewModel.RefreshEnvironmentAsync();
        ReportBox.Text = viewModel.BuildRedactedDiagnosticReport();
    }
    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Copy_OnClick(object sender, RoutedEventArgs e) { Clipboard.SetText(ReportBox.Text); StatusText.Text = "脱敏报告已复制到剪贴板。"; }
    private void OpenLogs_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppFileLogger.LogDirectory);
        Process.Start(new ProcessStartInfo(AppFileLogger.LogDirectory) { UseShellExecute = true });
    }
    private void Export_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "导出脱敏诊断报告", Filter = "文本文件|*.txt", FileName = $"SubtitleTranslator-diagnostics-{DateTime.Now:yyyyMMdd-HHmm}.txt" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        File.WriteAllText(dialog.FileName, ReportBox.Text);
        StatusText.Text = $"报告已导出：{dialog.FileName}";
    }
}
