using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SubtitleTranslator.App;

public partial class WorkbenchDashboard : UserControl
{
    public WorkbenchDashboard() => InitializeComponent();

    private MainWindow? MainWindow => Window.GetWindow(this) as MainWindow;
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private void WorkbenchScroll_OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        DashboardContentGrid.MinHeight = Math.Max(0, e.NewSize.Height - 52);

    private async void SelectVideo_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要生成字幕的视频",
            Filter = "视频文件|*.mkv;*.mp4;*.avi;*.mov;*.wmv;*.webm;*.m4v|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(MainWindow) == true && ViewModel is not null)
            await ViewModel.SelectVideoAsync(dialog.FileName);
    }

    private void DropArea_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = HasSingleFile(e) ? DragDropEffects.Copy : DragDropEffects.None;
        if (e.Effects == DragDropEffects.Copy) DropArea.BorderThickness = new Thickness(2);
        e.Handled = true;
    }

    private void DropArea_OnDragLeave(object sender, DragEventArgs e) => DropArea.BorderThickness = new Thickness(1);

    private async void DropArea_OnDrop(object sender, DragEventArgs e)
    {
        DropArea.BorderThickness = new Thickness(1);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 } files && ViewModel is not null)
            await ViewModel.SelectVideoAsync(files[0]);
    }

    private static bool HasSingleFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };

    private void OpenBatch_OnClick(object sender, RoutedEventArgs e) => MainWindow?.OpenBatchQueue_OnClick(sender, e);
    private void OpenProjects_OnClick(object sender, RoutedEventArgs e) => MainWindow?.OpenProjectHistory_OnClick(sender, e);
    private void OpenResources_OnClick(object sender, RoutedEventArgs e) => MainWindow?.OpenResources_OnClick(sender, e);
    private void OpenProjectsFolder_OnClick(object sender, RoutedEventArgs e) => MainWindow?.OpenProjectsFolder_OnClick(sender, e);
    private void Resume_OnClick(object sender, RoutedEventArgs e) => MainWindow?.ResumeRecentProject_OnClick(sender, e);
}
