using System.Diagnostics;
using System.IO;
using System.Windows;

namespace SubtitleTranslator.App;

public partial class ProjectHistoryWindow : Window
{
    private readonly ProjectHistoryViewModel viewModel = new();
    public string? ResumeSourcePath { get; private set; }

    public ProjectHistoryWindow()
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void Window_OnLoaded(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();
    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();

    private void Resume_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        if (!project.SourceExists)
        {
            MessageBox.Show(this, "原视频已经移动或删除，无法恢复。请把视频放回原路径。", "无法恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        ResumeSourcePath = project.SourcePath;
        DialogResult = true;
    }

    private void OpenSubtitle_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        var subtitle = Directory.Exists(project.ProjectDirectory)
            ? Directory.EnumerateFiles(project.ProjectDirectory, "*.srt", SearchOption.AllDirectories)
                .OrderByDescending(x => x.Contains("bilingual", StringComparison.OrdinalIgnoreCase)).FirstOrDefault()
            : null;
        if (subtitle is null) MessageBox.Show(this, "该项目尚未生成字幕。", "没有字幕", MessageBoxButton.OK, MessageBoxImage.Information);
        else new SubtitleEditorWindow(subtitle, project.SourcePath) { Owner = this }.ShowDialog();
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is not null && Directory.Exists(project.ProjectDirectory))
            Process.Start(new ProcessStartInfo(project.ProjectDirectory) { UseShellExecute = true });
    }

    private async void DeleteCache_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        if (MessageBox.Show(this, "将删除本项目的识别、翻译等缓存。字幕导出和项目记录会保留；下次继续时可能重新调用 DeepSeek 并产生费用。是否继续？",
                "清理项目缓存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        viewModel.Service.DeleteCache(project);
        await viewModel.RefreshAsync();
    }

    private async void DeleteProject_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        if (MessageBox.Show(this, $"将永久删除项目“{project.Name}”的记录、缓存和导出字幕，但不会删除原视频。是否继续？",
                "删除整个项目", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        viewModel.Service.DeleteProject(project);
        await viewModel.RefreshAsync();
    }

    private ProjectHistoryItem? RequireSelection()
    {
        if (viewModel.SelectedProject is not null) return viewModel.SelectedProject;
        MessageBox.Show(this, "请先选择一个项目。", "项目历史", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
