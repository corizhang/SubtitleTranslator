using Microsoft.Win32;
using System.Windows;
using System.ComponentModel;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;

namespace SubtitleTranslator.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly MainWindowViewModel viewModel;
    private bool startupHandled;
    private BatchQueuePage? batchQueuePage;
    private ProjectLibraryPage? projectLibraryPage;
    private ResourceManagementPage? resourceManagementPage;
    private SettingsPage? settingsPage;
    private DiagnosticsPage? diagnosticsPage;

    public MainWindow()
    {
        InitializeComponent();
        viewModel = new MainWindowViewModel();
        DataContext = viewModel;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (startupHandled) return;
        startupHandled = true;
        await viewModel.InitializeAsync();
        if (viewModel.NeedsInitialSetup) ShowSetupWizard();
    }

    private void OpenSetupWizard_OnClick(object sender, RoutedEventArgs e) => ShowSetupWizard();

    private async void NavigateWorkbench_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.RefreshRecentProjectsAsync();
        ShowPage(null, WorkbenchNavigation);
    }

    internal void OpenResources_OnClick(object sender, RoutedEventArgs e)
    {
        resourceManagementPage ??= new ResourceManagementPage(viewModel, ShowSetupWizard);
        ShowPage(resourceManagementPage, ResourcesNavigation);
    }

    private void OpenSettings_OnClick(object sender, RoutedEventArgs e)
    {
        settingsPage ??= new SettingsPage(viewModel);
        ShowPage(settingsPage, SettingsNavigation);
    }

    private void OpenDiagnostics_OnClick(object sender, RoutedEventArgs e)
    {
        diagnosticsPage ??= new DiagnosticsPage(viewModel);
        ShowPage(diagnosticsPage, DiagnosticsNavigation);
    }

    internal void OpenProjectsFolder_OnClick(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(viewModel.ProjectStoragePath);
        Process.Start(new ProcessStartInfo(viewModel.ProjectStoragePath) { UseShellExecute = true });
    }

    internal void OpenBatchQueue_OnClick(object sender, RoutedEventArgs e)
    {
        batchQueuePage ??= new BatchQueuePage(viewModel);
        ShowPage(batchQueuePage, BatchNavigation);
    }

    internal void OpenProjectHistory_OnClick(object sender, RoutedEventArgs e)
    {
        projectLibraryPage ??= new ProjectLibraryPage(viewModel, () => ShowPage(null, WorkbenchNavigation), ShowSubtitleEditorPage);
        ShowPage(projectLibraryPage, ProjectsNavigation);
    }

    private void ShowSubtitleEditorPage(string subtitlePath, string videoPath, string? projectDirectory)
    {
        var editor = new SubtitleEditorPage(subtitlePath, videoPath, projectDirectory, viewModel.VlcRuntimePath,
            () => ShowPage(projectLibraryPage, ProjectsNavigation));
        ShowPage(editor, ProjectsNavigation);
    }

    internal async void ResumeRecentProject_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectHistoryItem project }) return;
        if (viewModel.IsRunning)
        {
            MessageBox.Show(this, "已有任务正在处理，请等待完成或先取消当前任务。", "任务处理中", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (!project.SourceExists)
        {
            MessageBox.Show(this, "原视频已经移动或删除，请把视频放回原路径后再继续。", "无法继续", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await viewModel.ResumeProjectAsync(project);
        ShowPage(null, WorkbenchNavigation);
        await viewModel.StartPreparedTaskAsync();
    }

    private void ShowPage(UserControl? page, Button selectedNavigation)
    {
        WorkbenchPage.Visibility = page is null ? Visibility.Visible : Visibility.Collapsed;
        PageHost.Content = page;
        PageHost.Visibility = page is null ? Visibility.Collapsed : Visibility.Visible;

        foreach (var button in new[] { WorkbenchNavigation, BatchNavigation, ProjectsNavigation, ResourcesNavigation, SettingsNavigation, DiagnosticsNavigation })
            button.Style = (Style)FindResource("NavigationButtonStyle");
        selectedNavigation.Style = (Style)FindResource("SelectedNavigationButtonStyle");
    }

    private void ShowSetupWizard()
    {
        var wizard = new SetupWizardWindow(viewModel) { Owner = this };
        wizard.ShowDialog();
    }

    private async void SelectVideo_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要生成字幕的视频",
            Filter = "视频文件|*.mkv;*.mp4;*.avi;*.mov;*.wmv;*.webm;*.m4v|所有文件|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
            await viewModel.SelectVideoAsync(dialog.FileName);
    }

    private async void SelectOutputDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择字幕输出目录" };
        if (dialog.ShowDialog(this) == true)
            await viewModel.SelectCustomOutputDirectoryAsync(dialog.FolderName);
    }

    private async void SelectModel_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Whisper GGML 模型",
            Filter = "Whisper GGML 模型|*.bin|所有文件|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
            await viewModel.SelectLocalModelAsync(dialog.FileName);
    }

    private async void SelectFfmpeg_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 ffmpeg.exe", Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog(this) == true) await viewModel.SelectFfmpegAsync(dialog.FileName);
    }

    private async void SelectVad_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Silero VAD 模型", Filter = "VAD 模型|*.bin|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) await viewModel.SelectVadAsync(dialog.FileName);
    }

    private async void SelectRuntime_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 whisper.dll 的运行组件目录" };
        if (dialog.ShowDialog(this) == true) await viewModel.SelectRuntimeAsync(dialog.FolderName);
    }

    private async void RemoveComponents_OnClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "只会删除本应用下载到用户组件目录中的 VAD 和 Whisper runtime，不会删除你手动选择的外部文件。是否继续？",
            "移除已安装组件", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer == MessageBoxResult.Yes) await viewModel.RemoveManagedComponentsAsync();
    }

    private void DropArea_OnDragEnter(object sender, DragEventArgs e) =>
        e.Effects = HasSingleFile(e) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void DropArea_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            await viewModel.SelectVideoAsync(files[0]);
    }

    private static bool HasSingleFile(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) &&
        e.Data.GetData(DataFormats.FileDrop) is string[] { Length: 1 };

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        // InitializeComponent can fail before the view model is assigned. Keep shutdown safe
        // so the original XAML error remains the only failure in the log.
        if (viewModel is null) return;
        viewModel.Cancel();
        batchQueuePage?.Cancel();
        try { Task.Run(viewModel.SavePublicationSettingsAsync).GetAwaiter().GetResult(); }
        catch (Exception exception) { AppFileLogger.Error("保存字幕发布设置失败", exception); }
    }
}
