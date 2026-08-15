using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace SubtitleTranslator.App;

public partial class SettingsPage : UserControl
{
    private readonly MainWindowViewModel viewModel;
    public SettingsPage(MainWindowViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel; }
    private async void Save_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveDefaultSettingsAsync();
        MessageBox.Show(Window.GetWindow(this), "默认任务与字幕交付设置已保存。", "设置已保存", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private async void SelectOutputDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择默认字幕输出目录" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.SelectCustomOutputDirectoryAsync(dialog.FolderName);
    }
}
