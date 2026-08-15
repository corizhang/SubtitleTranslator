using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SubtitleTranslator.App;

public partial class BatchQueuePage : UserControl
{
    private readonly BatchQueueViewModel viewModel;
    private bool loaded;

    public BatchQueuePage(MainWindowViewModel main)
    {
        InitializeComponent();
        viewModel = new BatchQueueViewModel(main);
        DataContext = viewModel;
    }

    public void Cancel() => viewModel.Cancel();
    private async void Page_OnLoaded(object sender, RoutedEventArgs e) { if (!loaded) { loaded = true; await viewModel.LoadAsync(); } }
    private async void Add_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Filter = "视频文件|*.mkv;*.mp4;*.avi;*.mov;*.wmv;*.webm;*.m4v|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) await viewModel.AddFilesAsync(dialog.FileNames);
    }
    private async void Remove_OnClick(object sender, RoutedEventArgs e) => await viewModel.RemoveSelectedAsync();
    private async void Start_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync();
    private async void Retry_OnClick(object sender, RoutedEventArgs e) => await viewModel.StartAsync(true);
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => viewModel.Cancel();
    private void OpenSubtitle_OnClick(object sender, RoutedEventArgs e) => viewModel.OpenSubtitle();
    private void Page_OnDragEnter(object sender, DragEventArgs e) => e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
    private async void Page_OnDrop(object sender, DragEventArgs e) { if (!viewModel.IsRunning && e.Data.GetData(DataFormats.FileDrop) is string[] files) await viewModel.AddFilesAsync(files); }
}
