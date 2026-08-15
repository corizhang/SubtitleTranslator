using Microsoft.Win32;
using System.Windows;
using System.ComponentModel;

namespace SubtitleTranslator.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private bool startupHandled;

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

    private void Window_OnClosing(object? sender, CancelEventArgs e) => viewModel.Cancel();
}
