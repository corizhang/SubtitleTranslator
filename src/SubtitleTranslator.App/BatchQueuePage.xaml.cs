using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SubtitleTranslator.App;

public partial class BatchQueuePage : UserControl
{
    private readonly BatchQueueViewModel viewModel;
    private readonly Action openProjects;
    private bool loaded;

    public BatchQueuePage(MainWindowViewModel main, Action openProjects)
    {
        InitializeComponent();
        viewModel = new BatchQueueViewModel(main);
        this.openProjects = openProjects;
        DataContext = viewModel;
    }

    public void Cancel() => viewModel.Cancel();
    private async void Page_OnLoaded(object sender, RoutedEventArgs e) { if (!loaded) { loaded = true; await viewModel.LoadAsync(); } }
    private async void Add_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "视频文件|*.mkv;*.mp4;*.avi;*.mov;*.wmv;*.webm;*.m4v|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.AddFilesAsync(dialog.FileNames);
    }
    private async void AddFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择包含视频的文件夹", Multiselect = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        await viewModel.AddFilesAsync(Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.TopDirectoryOnly));
    }
    private async void Preflight_OnClick(object sender, RoutedEventArgs e) => await viewModel.RerunPreflightAsync();
    private async void Remove_OnClick(object sender, RoutedEventArgs e) => await viewModel.RemoveSelectedAsync();
    private async void ClearCompleted_OnClick(object sender, RoutedEventArgs e) => await viewModel.ClearCompletedAsync();
    private async void Archive_OnClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.NeedsAttentionCount > 0 && MessageBox.Show(Window.GetWindow(this),
            "当前批次仍有失败、取消或无法处理的项目。仍要结束并归档吗？", "归档当前批次",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await viewModel.ArchiveAndCreateNewAsync();
    }
    private async void RecreateFailed_OnClick(object sender, RoutedEventArgs e) => await viewModel.RecreateFromArchiveAsync(true);
    private async void RecreateAll_OnClick(object sender, RoutedEventArgs e) => await viewModel.RecreateFromArchiveAsync(false);
    private async void DeleteArchive_OnClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedArchive is null || MessageBox.Show(Window.GetWindow(this),
            "只删除这条批次执行记录，不会删除项目和字幕文件。是否继续？", "删除历史批次",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await viewModel.DeleteSelectedArchiveAsync();
    }
    private void OpenProjects_OnClick(object sender, RoutedEventArgs e) => openProjects();
    private async void Start_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync();
    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync(true);
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => viewModel.Cancel();
    private void OpenResult_OnClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.SelectedItem?.IsCompleted == true) openProjects();
        else viewModel.OpenSubtitle();
    }
    private void Page_OnDragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Page_OnDrop(object sender, DragEventArgs e) { if (!viewModel.IsRunning && e.Data.GetData(DataFormats.FileDrop) is string[] files) await viewModel.AddFilesAsync(files); }
}
