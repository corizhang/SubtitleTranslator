using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;

namespace SubtitleTranslator.App;

public partial class BatchQueueWindow : Window
{
    private readonly BatchQueueViewModel viewModel;
    public BatchQueueWindow(MainWindowViewModel main)
    { InitializeComponent(); viewModel = new BatchQueueViewModel(main); DataContext = viewModel; }
    private async void Window_OnLoaded(object sender, RoutedEventArgs e) => await viewModel.LoadAsync();
    private async void Add_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "视频文件|*.mkv;*.mp4;*.avi;*.mov;*.wmv;*.webm;*.m4v|所有文件|*.*" };
        if (dialog.ShowDialog(this) == true) await viewModel.AddFilesAsync(dialog.FileNames);
    }
    private async void Remove_OnClick(object sender, RoutedEventArgs e) => await viewModel.RemoveSelectedAsync();
    private async void Start_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync();
    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync(true);
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => viewModel.Cancel();
    private void OpenSubtitle_OnClick(object sender, RoutedEventArgs e) => viewModel.OpenSubtitle();
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (!viewModel.IsRunning) return;
        viewModel.Cancel(); e.Cancel = true;
        MessageBox.Show(this, "正在停止当前任务，请稍候；队列状态会自动保存。", "正在停止", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Window_OnDragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Window_OnDrop(object sender, DragEventArgs e)
    { if (!viewModel.IsRunning && e.Data.GetData(DataFormats.FileDrop) is string[] files) await viewModel.AddFilesAsync(files); }
}
