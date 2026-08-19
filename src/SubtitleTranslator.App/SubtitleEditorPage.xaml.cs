using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using SubtitleTranslator.Infrastructure;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.App;

public partial class SubtitleEditorPage : UserControl
{
    private readonly SubtitleEditorViewModel viewModel = new();
    private readonly string videoPath;
    private readonly string subtitlePath;
    private readonly string? projectDirectory;
    private readonly Action goBack;
    private readonly DispatcherTimer timer;
    private bool mediaAvailable;
    private bool playing;

    public SubtitleEditorPage(string subtitlePath, string videoPath, string? projectDirectory, Action goBack)
    {
        InitializeComponent();
        this.subtitlePath = Path.GetFullPath(subtitlePath);
        this.videoPath = videoPath;
        this.projectDirectory = projectDirectory;
        this.goBack = goBack;
        DataContext = viewModel;
        ProjectTitleText.Text = Path.GetFileNameWithoutExtension(videoPath);
        SubtitlePathText.Text = this.subtitlePath;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) => UpdatePlaybackPosition();
    }

    private async void Page_OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await viewModel.LoadAsync(subtitlePath);
            UpdateFilterButtons();
            if (File.Exists(videoPath)) VideoPlayer.Source = new Uri(videoPath);
            else ShowPlayerFallback("原视频已经移动或删除");
        }
        catch (Exception exception) { ShowPlayerFallback(exception.Message); }
    }

    private void Page_OnUnloaded(object sender, RoutedEventArgs e) { timer.Stop(); VideoPlayer.Stop(); VideoPlayer.Close(); }
    private void Back_OnClick(object sender, RoutedEventArgs e)
    {
        if (viewModel.IsDirty && MessageBox.Show(
                "当前修改尚未保存，确定返回项目库吗？",
                "未保存的字幕修改",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        goBack();
    }
    private void MediaOpened_OnHandler(object sender, RoutedEventArgs e)
    {
        mediaAvailable = true; PlayerFallback.Visibility = Visibility.Collapsed; VideoPlayer.Visibility = Visibility.Visible;
        if (VideoPlayer.NaturalDuration.HasTimeSpan)
        {
            PlaybackSlider.Maximum = VideoPlayer.NaturalDuration.TimeSpan.TotalMilliseconds;
            PlaybackDurationText.Text = $"{VideoPlayer.NaturalDuration.TimeSpan:hh\\:mm\\:ss}";
        }
        SeekToSelectedCue();
    }
    private void MediaFailed_OnHandler(object sender, ExceptionRoutedEventArgs e) => ShowPlayerFallback("内置预览不支持当前视频编码，请使用外部播放器");
    private void ShowPlayerFallback(string message) { mediaAvailable = false; VideoPlayer.Visibility = Visibility.Collapsed; PlayerFallback.Visibility = Visibility.Visible; PlayerFallbackText.Text = message; }
    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (!mediaAvailable) { OpenVideo(); return; }
        if (playing) { VideoPlayer.Pause(); timer.Stop(); } else { VideoPlayer.Play(); timer.Start(); }
        playing = !playing; PlayButton.Content = playing ? "暂停" : "播放";
    }
    private void OpenExternal_OnClick(object sender, RoutedEventArgs e) => OpenVideo();
    private void OpenVideo() { if (File.Exists(videoPath)) Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true }); }
    private void CueGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e) => SeekToSelectedCue();
    private void SeekToSelectedCue()
    {
        if (viewModel.SelectedCue is null) return;
        if (mediaAvailable && SrtDocumentService.TryParseTimestamp(viewModel.SelectedCue.StartText, out var start))
        { VideoPlayer.Position = start; UpdatePlaybackPosition(); }
    }
    private void UpdatePlaybackPosition()
    {
        PlaybackSlider.Value = VideoPlayer.Position.TotalMilliseconds;
        PlaybackTimeText.Text = $"{VideoPlayer.Position:hh\\:mm\\:ss\\.fff}";
    }
    private void PreviousIssue_OnClick(object sender, RoutedEventArgs e) { viewModel.Validate(); viewModel.SelectIssue(-1); CueGrid.ScrollIntoView(viewModel.SelectedCue); SeekToSelectedCue(); }
    private void NextIssue_OnClick(object sender, RoutedEventArgs e) { viewModel.Validate(); viewModel.SelectIssue(1); CueGrid.ScrollIntoView(viewModel.SelectedCue); SeekToSelectedCue(); }
    private void Filter_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string filter })
        {
            viewModel.IssueFilter = filter;
            UpdateFilterButtons();
        }
    }
    private void UpdateFilterButtons()
    {
        var buttons = new[] { AllFilterButton, ErrorFilterButton, SuggestionFilterButton, ModifiedFilterButton };
        foreach (var button in buttons) button.IsChecked = Equals(button.Tag, viewModel.IssueFilter);
    }
    private void PreviousCue_OnClick(object sender, RoutedEventArgs e) => SelectAdjacentCue(-1);
    private void NextCue_OnClick(object sender, RoutedEventArgs e) => SelectAdjacentCue(1);
    private void SelectAdjacentCue(int direction)
    {
        if (viewModel.SelectedCue is null || viewModel.Cues.Count == 0) return;
        var index = viewModel.Cues.IndexOf(viewModel.SelectedCue);
        index = Math.Clamp(index + direction, 0, viewModel.Cues.Count - 1);
        viewModel.SelectedCue = viewModel.Cues[index];
        CueGrid.ScrollIntoView(viewModel.SelectedCue);
        SeekToSelectedCue();
    }
    private void CenterWorkspace_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var availableHeight = Math.Max(260, e.NewSize.Height - 200);
        var maximumHeight = (double)FindResource("Size.Editor.VideoMaxHeight");
        VideoFrame.Height = Math.Min(maximumHeight, Math.Min(e.NewSize.Width * 9d / 16d, availableHeight));
    }
    private void Validate_OnClick(object sender, RoutedEventArgs e) => viewModel.Validate();
    private void NudgeStart_OnClick(object sender, RoutedEventArgs e) => Nudge(true, sender is Button { Tag: string value } ? int.Parse(value) : 0);
    private void NudgeEnd_OnClick(object sender, RoutedEventArgs e) => Nudge(false, sender is Button { Tag: string value } ? int.Parse(value) : 0);
    private void Nudge(bool start, int milliseconds)
    {
        if (viewModel.SelectedCue is null) return;
        var current = start ? viewModel.SelectedCue.StartText : viewModel.SelectedCue.EndText;
        if (!SrtDocumentService.TryParseTimestamp(current, out var time)) return;
        var next = time + TimeSpan.FromMilliseconds(milliseconds);
        var formatted = SrtDocumentService.FormatTimestamp(next < TimeSpan.Zero ? TimeSpan.Zero : next);
        if (start) viewModel.SelectedCue.StartText = formatted; else viewModel.SelectedCue.EndText = formatted;
        SeekToSelectedCue();
    }
    private async void Save_OnClick(object sender, RoutedEventArgs e) => await SaveAsync(false);
    private async void SavePublish_OnClick(object sender, RoutedEventArgs e) => await SaveAsync(true);
    private async Task SaveAsync(bool publish)
    {
        try
        {
            await viewModel.SaveAsync(subtitlePath);
            if (!publish) return;
            if (string.IsNullOrWhiteSpace(projectDirectory)) throw new InvalidOperationException("当前字幕没有关联项目发布记录。");
            var receipt = await new SubtitlePublicationService().RepublishAsync(projectDirectory, subtitlePath, CancellationToken.None);
            MessageBox.Show(Window.GetWindow(this), receipt.Message, receipt.Success ? "发布完成" : "发布失败", MessageBoxButton.OK, receipt.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception exception) { MessageBox.Show(Window.GetWindow(this), exception.Message, "无法保存字幕", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
