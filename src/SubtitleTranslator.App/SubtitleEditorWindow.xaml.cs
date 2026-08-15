using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.App;

public partial class SubtitleEditorWindow : Window
{
    private readonly SubtitleEditorViewModel viewModel = new();
    private readonly string videoPath;
    private string subtitlePath;
    private readonly string? projectDirectory;
    private bool allowClose;

    public SubtitleEditorWindow(string subtitlePath, string videoPath, string? projectDirectory = null)
    {
        InitializeComponent();
        this.subtitlePath = Path.GetFullPath(subtitlePath);
        this.videoPath = videoPath;
        this.projectDirectory = projectDirectory;
        DataContext = viewModel;
        FilePathText.Text = this.subtitlePath;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e)
    {
        try { await viewModel.LoadAsync(subtitlePath); }
        catch (Exception exception)
        {
            AppFileLogger.Error("打开字幕校订中心失败", exception);
            MessageBox.Show(this, exception.Message, "无法读取字幕", MessageBoxButton.OK, MessageBoxImage.Error);
            allowClose = true;
            Close();
        }
    }

    private void Validate_OnClick(object sender, RoutedEventArgs e) => viewModel.Validate();
    private void PreviousIssue_OnClick(object sender, RoutedEventArgs e) { viewModel.Validate(); viewModel.SelectIssue(-1); CueGrid.ScrollIntoView(viewModel.SelectedCue); }
    private void NextIssue_OnClick(object sender, RoutedEventArgs e) { viewModel.Validate(); viewModel.SelectIssue(1); CueGrid.ScrollIntoView(viewModel.SelectedCue); }

    private async void Save_OnClick(object sender, RoutedEventArgs e) => await SaveAsync(subtitlePath);

    private async void SaveAndPublish_OnClick(object sender, RoutedEventArgs e)
    {
        if (!await SaveAsync(subtitlePath)) return;
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            MessageBox.Show(this, "当前字幕没有关联项目发布记录，请使用“另存为”。", "无法发布", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var receipt = await new SubtitlePublicationService().RepublishAsync(projectDirectory, subtitlePath, CancellationToken.None);
            MessageBox.Show(this, receipt.Message, receipt.Success ? "发布完成" : "发布失败", MessageBoxButton.OK,
                receipt.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法发布", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void SaveAs_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "SubRip 字幕 (*.srt)|*.srt", FileName = Path.GetFileNameWithoutExtension(subtitlePath) + ".edited.srt", DefaultExt = ".srt" };
        if (dialog.ShowDialog(this) == true && await SaveAsync(dialog.FileName))
        {
            subtitlePath = dialog.FileName;
            FilePathText.Text = subtitlePath;
        }
    }

    private async Task<bool> SaveAsync(string path)
    {
        try { await viewModel.SaveAsync(path); return true; }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "无法保存字幕", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenVideo_OnClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(videoPath))
        {
            MessageBox.Show(this, "原视频已经移动或删除。", "无法打开视频", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose || !viewModel.IsDirty) return;
        var result = MessageBox.Show(this, "字幕有尚未保存的修改，确定关闭吗？", "未保存的修改", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.No) e.Cancel = true;
    }
}
