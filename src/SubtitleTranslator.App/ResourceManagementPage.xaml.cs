using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace SubtitleTranslator.App;

public partial class ResourceManagementPage : UserControl
{
    private readonly MainWindowViewModel viewModel;
    private readonly Action openSetupWizard;
    private bool loaded;

    public ResourceManagementPage(MainWindowViewModel viewModel, Action openSetupWizard)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.openSetupWizard = openSetupWizard;
        DataContext = viewModel;
    }

    private async void Page_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (loaded) return;
        loaded = true;
        await viewModel.RefreshEnvironmentAsync();
    }

    private void OpenWizard_OnClick(object sender, RoutedEventArgs e) => openSetupWizard();
    private async void SelectFfmpeg_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 ffmpeg.exe", Filter = "FFmpeg|ffmpeg.exe|可执行文件|*.exe" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.SelectFfmpegAsync(dialog.FileName);
    }
    private async void SelectRuntime_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 whisper.dll 的 runtime 目录" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.SelectRuntimeAsync(dialog.FolderName);
    }
    private async void SelectVad_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Silero VAD 模型", Filter = "VAD 模型|*.bin|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.SelectVadAsync(dialog.FileName);
    }
    private async void SelectModel_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Whisper GGML 模型", Filter = "Whisper GGML 模型|*.bin|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.SelectLocalModelAsync(dialog.FileName);
    }
    private async void SaveApiKey_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveApiKeyAsync(DeepSeekApiKeyBox.Password);
        DeepSeekApiKeyBox.Clear();
    }
    private async void RemoveManaged_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Window.GetWindow(this), "只删除由本应用下载的 Runtime 与 VAD，不删除你手动选择的外部文件。是否继续？", "移除应用管理的组件", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            await viewModel.RemoveManagedComponentsAsync();
    }
}
