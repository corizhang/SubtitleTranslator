using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SubtitleTranslator.App;

public partial class SetupWizardWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private readonly TextBlock[] stepLabels;

    public SetupWizardWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        stepLabels = [StepGpu, StepFfmpeg, StepRuntime, StepModel, StepDeepSeek];
        WizardTabs.SelectedIndex = 0;
        UpdateNavigation();
    }

    private async void Next_OnClick(object sender, RoutedEventArgs e)
    {
        SetNavigationEnabled(false);
        ErrorText.Text = await viewModel.ValidateSetupStepAsync(WizardTabs.SelectedIndex) ?? string.Empty;
        SetNavigationEnabled(true);
        if (!string.IsNullOrEmpty(ErrorText.Text)) return;

        if (WizardTabs.SelectedIndex == WizardTabs.Items.Count - 1)
        {
            DialogResult = true;
            return;
        }

        WizardTabs.SelectedIndex++;
        UpdateNavigation();
    }

    private void Previous_OnClick(object sender, RoutedEventArgs e)
    {
        if (WizardTabs.SelectedIndex > 0) WizardTabs.SelectedIndex--;
        ErrorText.Text = string.Empty;
        UpdateNavigation();
    }

    private void Skip_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

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
        var dialog = new OpenFolderDialog { Title = "选择包含 whisper.dll 的 runtime 目录" };
        if (dialog.ShowDialog(this) == true) await viewModel.SelectRuntimeAsync(dialog.FolderName);
    }

    private async void SelectModel_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择 Whisper GGML 模型", Filter = "Whisper GGML 模型|*.bin|所有文件|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await viewModel.SelectLocalModelAsync(dialog.FileName);
    }

    private async void SaveApiKey_OnClick(object sender, RoutedEventArgs e)
    {
        await viewModel.SaveApiKeyAsync(DeepSeekApiKeyBox.Password);
        DeepSeekApiKeyBox.Clear();
    }

    private void UpdateNavigation()
    {
        PreviousButton.IsEnabled = WizardTabs.SelectedIndex > 0;
        NextButton.Content = WizardTabs.SelectedIndex == WizardTabs.Items.Count - 1 ? "完成" : "下一步";
        for (var i = 0; i < stepLabels.Length; i++)
        {
            stepLabels[i].Foreground = i == WizardTabs.SelectedIndex
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(174, 185, 208));
            stepLabels[i].FontWeight = i == WizardTabs.SelectedIndex ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void SetNavigationEnabled(bool enabled)
    {
        PreviousButton.IsEnabled = enabled && WizardTabs.SelectedIndex > 0;
        NextButton.IsEnabled = enabled;
    }
}
