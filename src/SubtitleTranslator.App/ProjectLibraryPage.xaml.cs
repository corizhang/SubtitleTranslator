using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace SubtitleTranslator.App;

public partial class ProjectLibraryPage : UserControl
{
    private readonly ProjectHistoryViewModel viewModel = new();
    private readonly MainWindowViewModel mainViewModel;
    private readonly Action showWorkbench;
    private bool loaded;

    public ProjectLibraryPage(MainWindowViewModel mainViewModel, Action showWorkbench)
    {
        InitializeComponent();
        this.mainViewModel = mainViewModel;
        this.showWorkbench = showWorkbench;
        DataContext = viewModel;
    }

    private async void Page_OnLoaded(object sender, RoutedEventArgs e) { if (!loaded) { loaded = true; await viewModel.RefreshAsync(); } }
    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await viewModel.RefreshAsync();

    private async void Resume_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        if (!project.SourceExists)
        {
            MessageBox.Show(Window.GetWindow(this), "原视频已经移动或删除，无法恢复。请把视频放回原路径。", "无法恢复", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await mainViewModel.SelectVideoAsync(project.SourcePath);
        showWorkbench();
        MessageBox.Show(Window.GetWindow(this), "已加载原视频。开始生成后会复用有效缓存；若翻译缓存已清理，可能再次产生 DeepSeek API 费用。", "任务已准备恢复", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSubtitle_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        var subtitle = Directory.Exists(project.ProjectDirectory) ? Directory.EnumerateFiles(project.ProjectDirectory, "*.srt", SearchOption.AllDirectories).OrderByDescending(x => x.Contains("bilingual", StringComparison.OrdinalIgnoreCase)).FirstOrDefault() : null;
        if (subtitle is null) MessageBox.Show(Window.GetWindow(this), "该项目尚未生成字幕。", "没有字幕", MessageBoxButton.OK, MessageBoxImage.Information);
        else new SubtitleEditorWindow(subtitle, project.SourcePath, project.ProjectDirectory) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void Republish_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null) return;
        try
        {
            var receipt = await viewModel.Service.RepublishAsync(project, CancellationToken.None);
            viewModel.SetMessage(receipt.Message);
            MessageBox.Show(Window.GetWindow(this), receipt.Message, receipt.Success ? "发布完成" : "发布失败", MessageBoxButton.OK, receipt.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception exception) { MessageBox.Show(Window.GetWindow(this), exception.Message, "无法重新发布", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is not null && Directory.Exists(project.ProjectDirectory)) Process.Start(new ProcessStartInfo(project.ProjectDirectory) { UseShellExecute = true });
    }

    private async void DeleteCache_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null || MessageBox.Show(Window.GetWindow(this), "将删除本项目的识别、翻译等缓存。字幕导出和项目记录会保留；下次继续时可能重新调用 DeepSeek 并产生费用。是否继续？", "清理项目缓存", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        viewModel.Service.DeleteCache(project);
        await viewModel.RefreshAsync();
    }

    private async void DeleteProject_OnClick(object sender, RoutedEventArgs e)
    {
        var project = RequireSelection();
        if (project is null || MessageBox.Show(Window.GetWindow(this), $"将永久删除项目“{project.Name}”的记录、缓存和导出字幕，但不会删除原视频。是否继续？", "删除整个项目", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        viewModel.Service.DeleteProject(project);
        await viewModel.RefreshAsync();
    }

    private ProjectHistoryItem? RequireSelection()
    {
        if (viewModel.SelectedProject is not null) return viewModel.SelectedProject;
        MessageBox.Show(Window.GetWindow(this), "请先选择一个项目。", "项目库", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }
}
