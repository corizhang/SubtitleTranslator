using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using VlcMedia = LibVLCSharp.Shared.Media;
using SubtitleTranslator.Infrastructure;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.App;

public partial class SubtitleEditorPage : UserControl
{
    private readonly SubtitleEditorViewModel viewModel = new();
    private readonly string videoPath;
    private readonly string subtitlePath;
    private readonly string? projectDirectory;
    private readonly string? vlcRuntimePath;
    private readonly Action goBack;
    private readonly DispatcherTimer timer;
    private bool mediaAvailable;
    private bool playing;
    private LibVLC? libVlc;
    private MediaPlayer? vlcPlayer;
    private VlcMedia? vlcMedia;
    private bool usingVlc;
    private long pendingVlcSeek = -1;
    private bool syncingCueFromPlayback;
    private bool updatingPlaybackSlider;
    private bool draggingPlaybackSlider;
    private bool muted;
    private bool immersive;
    private double volumeBeforeMute = 100;
    private EditableSubtitleCue? loopTargetCue;

    public SubtitleEditorPage(string subtitlePath, string videoPath, string? projectDirectory, Action goBack)
        : this(subtitlePath, videoPath, projectDirectory, null, goBack) { }

    public SubtitleEditorPage(string subtitlePath, string videoPath, string? projectDirectory, string? vlcRuntimePath, Action goBack)
    {
        InitializeComponent();
        this.subtitlePath = Path.GetFullPath(subtitlePath);
        this.videoPath = videoPath;
        this.projectDirectory = projectDirectory;
        this.vlcRuntimePath = vlcRuntimePath;
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
            if (!File.Exists(videoPath)) ShowPlayerFallback("原视频已经移动或删除");
            else if (!TryInitializeVlc()) InitializeSystemPlayer();
        }
        catch (Exception exception) { ShowPlayerFallback(exception.Message); }
    }

    private void Page_OnUnloaded(object sender, RoutedEventArgs e)
    {
        timer.Stop();
        VideoPlayer.Stop(); VideoPlayer.Close();
        VlcVideoView.MediaPlayer = null;
        vlcPlayer?.Stop(); vlcMedia?.Dispose(); vlcPlayer?.Dispose(); libVlc?.Dispose();
        vlcMedia = null; vlcPlayer = null; libVlc = null;
    }
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
    private void MediaEnded_OnHandler(object sender, RoutedEventArgs e) { timer.Stop(); SetPlaying(false); }
    private void MediaFailed_OnHandler(object sender, ExceptionRoutedEventArgs e) => ShowPlayerFallback("内置预览不支持当前视频编码，请使用外部播放器");
    private bool TryInitializeVlc()
    {
        if (string.IsNullOrWhiteSpace(vlcRuntimePath)) return false;
        try
        {
            Core.Initialize(vlcRuntimePath);
            libVlc = new LibVLC(false, "--no-sub-autodetect-file", "--sub-track=-1");
            vlcPlayer = new MediaPlayer(libVlc);
            vlcPlayer.Volume = (int)VolumeSlider.Value;
            vlcMedia = new VlcMedia(libVlc, new Uri(videoPath));
            vlcMedia.AddOption(":no-sub-autodetect-file");
            vlcMedia.AddOption(":sub-track=-1");
            vlcPlayer.Media = vlcMedia;
            vlcPlayer.LengthChanged += (_, e) => Dispatcher.InvokeAsync(() =>
            {
                PlaybackSlider.Maximum = Math.Max(0, e.Length);
                PlaybackDurationText.Text = TimeSpan.FromMilliseconds(e.Length).ToString("hh\\:mm\\:ss");
            });
            vlcPlayer.Playing += (_, _) => Dispatcher.InvokeAsync(() =>
            {
                vlcPlayer.SetSpu(-1);
                if (pendingVlcSeek >= 0) { vlcPlayer.Time = pendingVlcSeek; pendingVlcSeek = -1; }
            });
            vlcPlayer.EncounteredError += (_, _) => Dispatcher.InvokeAsync(FallbackFromVlc);
            vlcPlayer.EndReached += (_, _) => Dispatcher.InvokeAsync(() => { timer.Stop(); SetPlaying(false); });
            VlcVideoView.MediaPlayer = vlcPlayer;
            usingVlc = true; mediaAvailable = true;
            VideoPlayer.Visibility = Visibility.Collapsed;
            SystemSubtitleOverlay.Visibility = Visibility.Collapsed;
            VlcVideoView.Visibility = Visibility.Visible;
            PlayerFallback.Visibility = Visibility.Collapsed;
            PlaybackEngineText.Text = "VLC 内嵌播放器";
            SeekToSelectedCue();
            return true;
        }
        catch (Exception exception)
        {
            AppFileLogger.Error("VLC 播放引擎初始化失败，回退到系统播放器。", exception);
            VlcVideoView.MediaPlayer = null;
            vlcMedia?.Dispose(); vlcPlayer?.Dispose(); libVlc?.Dispose();
            vlcMedia = null; vlcPlayer = null; libVlc = null; usingVlc = false;
            return false;
        }
    }
    private void InitializeSystemPlayer()
    {
        usingVlc = false; mediaAvailable = false;
        VlcVideoView.Visibility = Visibility.Collapsed;
        SystemSubtitleOverlay.Visibility = Visibility.Visible;
        VideoPlayer.Visibility = Visibility.Visible;
        PlayerFallback.Visibility = Visibility.Visible;
        PlayerFallbackText.Text = "正在使用系统播放器打开视频……";
        PlaybackEngineText.Text = "系统播放器";
        VideoPlayer.Source = new Uri(videoPath);
    }
    private void FallbackFromVlc()
    {
        if (!usingVlc) return;
        AppFileLogger.Info("VLC 无法播放当前媒体，回退到系统播放器。");
        timer.Stop(); SetPlaying(false);
        VlcVideoView.MediaPlayer = null;
        vlcPlayer?.Stop(); vlcMedia?.Dispose(); vlcPlayer?.Dispose(); libVlc?.Dispose();
        vlcMedia = null; vlcPlayer = null; libVlc = null;
        InitializeSystemPlayer();
    }
    private void ShowPlayerFallback(string message)
    {
        mediaAvailable = false; usingVlc = false;
        VideoPlayer.Visibility = Visibility.Collapsed; VlcVideoView.Visibility = Visibility.Collapsed;
        SystemSubtitleOverlay.Visibility = Visibility.Collapsed;
        PlayerFallback.Visibility = Visibility.Visible; PlayerFallbackText.Text = message;
        PlaybackEngineText.Text = "无法内嵌播放";
    }
    private void Play_OnClick(object sender, RoutedEventArgs e)
    {
        if (!mediaAvailable) { OpenVideo(); return; }
        if (playing)
        {
            if (usingVlc) vlcPlayer?.Pause(); else VideoPlayer.Pause();
            timer.Stop(); SetPlaying(false);
        }
        else
        {
            if (usingVlc) vlcPlayer?.Play(); else VideoPlayer.Play();
            timer.Start(); SetPlaying(true);
        }
    }
    private void OpenExternal_OnClick(object sender, RoutedEventArgs e) => OpenVideo();
    private void OpenVideo() { if (File.Exists(videoPath)) Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true }); }
    private void CueGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!syncingCueFromPlayback) SeekToSelectedCue();
    }
    private void SeekToSelectedCue()
    {
        if (viewModel.SelectedCue is null) return;
        if (mediaAvailable && SrtDocumentService.TryParseTimestamp(viewModel.SelectedCue.StartText, out var start))
        {
            SeekTo(start);
            if (LoopCueButton.IsChecked == true) loopTargetCue = viewModel.SelectedCue;
            UpdatePlaybackPosition();
        }
    }
    private void UpdatePlaybackPosition()
    {
        if (usingVlc && vlcPlayer is { Spu: not -1 }) vlcPlayer.SetSpu(-1);
        var position = GetPlaybackPosition();
        if (playing && ApplyCueLoop(position)) return;
        if (!draggingPlaybackSlider)
        {
            updatingPlaybackSlider = true;
            PlaybackSlider.Value = Math.Clamp(position.TotalMilliseconds, PlaybackSlider.Minimum, PlaybackSlider.Maximum);
            updatingPlaybackSlider = false;
        }
        PlaybackTimeText.Text = $"{position:hh\\:mm\\:ss\\.fff}";
        if (playing) SyncCueToPlayback(position);
    }
    private void SyncCueToPlayback(TimeSpan position)
    {
        var cue = viewModel.FindCueAt(position);
        VlcSubtitleOverlay.Visibility = usingVlc && cue is not null ? Visibility.Visible : Visibility.Collapsed;
        SystemSubtitleOverlay.Visibility = !usingVlc && cue is not null ? Visibility.Visible : Visibility.Collapsed;
        if (cue is null || ReferenceEquals(cue, viewModel.SelectedCue)) return;
        syncingCueFromPlayback = true;
        try
        {
            viewModel.SelectedCue = cue;
            CueGrid.ScrollIntoView(cue);
        }
        finally { syncingCueFromPlayback = false; }
    }
    private void SetPlaying(bool value)
    {
        playing = value;
        PlayIcon.Symbol = value ? Wpf.Ui.Controls.SymbolRegular.Pause24 : Wpf.Ui.Controls.SymbolRegular.Play24;
        if (!value)
        {
            VlcSubtitleOverlay.Visibility = usingVlc ? Visibility.Visible : Visibility.Collapsed;
            SystemSubtitleOverlay.Visibility = usingVlc ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    private TimeSpan GetPlaybackPosition() => usingVlc
        ? TimeSpan.FromMilliseconds(Math.Max(0, vlcPlayer?.Time ?? 0)) : VideoPlayer.Position;

    private void SeekTo(TimeSpan position)
    {
        var maximum = PlaybackSlider.Maximum > 0 ? TimeSpan.FromMilliseconds(PlaybackSlider.Maximum) : TimeSpan.MaxValue;
        var target = position < TimeSpan.Zero ? TimeSpan.Zero : position > maximum ? maximum : position;
        if (usingVlc && vlcPlayer is not null)
        {
            if (vlcPlayer.Length > 0 || vlcPlayer.IsPlaying) vlcPlayer.Time = (long)target.TotalMilliseconds;
            else pendingVlcSeek = (long)target.TotalMilliseconds;
        }
        else VideoPlayer.Position = target;
    }

    private void Skip(int seconds) { if (mediaAvailable) { SeekTo(GetPlaybackPosition() + TimeSpan.FromSeconds(seconds)); UpdatePlaybackPosition(); } }
    private void Skip_OnClick(object sender, RoutedEventArgs e)
    { if (sender is Button { Tag: string seconds } && int.TryParse(seconds, out var value)) Skip(value); }

    private void PlaybackSlider_OnDragStarted(object sender, MouseButtonEventArgs e) => draggingPlaybackSlider = true;
    private void PlaybackSlider_OnDragCompleted(object sender, MouseButtonEventArgs e)
    {
        if (!draggingPlaybackSlider) return;
        draggingPlaybackSlider = false;
        SeekTo(TimeSpan.FromMilliseconds(PlaybackSlider.Value));
        UpdatePlaybackPosition();
    }
    private void PlaybackSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingPlaybackSlider || !draggingPlaybackSlider) return;
        PlaybackTimeText.Text = TimeSpan.FromMilliseconds(e.NewValue).ToString("hh\\:mm\\:ss\\.fff");
    }

    private void VolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (e.NewValue > 0) { muted = false; volumeBeforeMute = e.NewValue; }
        ApplyVolume(muted ? 0 : e.NewValue);
        UpdateVolumeIcon();
    }
    private void Mute_OnClick(object sender, RoutedEventArgs e)
    {
        muted = !muted;
        if (!muted && VolumeSlider.Value <= 0) VolumeSlider.Value = Math.Max(25, volumeBeforeMute);
        ApplyVolume(muted ? 0 : VolumeSlider.Value);
        UpdateVolumeIcon();
    }
    private void ApplyVolume(double value)
    {
        if (vlcPlayer is not null) vlcPlayer.Volume = (int)Math.Round(value);
        VideoPlayer.Volume = Math.Clamp(value / 100d, 0, 1);
    }
    private void UpdateVolumeIcon() => VolumeIcon.Symbol = muted || VolumeSlider.Value <= 0
        ? Wpf.Ui.Controls.SymbolRegular.SpeakerMute24 : Wpf.Ui.Controls.SymbolRegular.Speaker224;

    private void SpeedSelector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedSelector.SelectedItem is not ComboBoxItem { Tag: string value } ||
            !double.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var speed)) return;
        vlcPlayer?.SetRate((float)speed);
        VideoPlayer.SpeedRatio = speed;
    }

    private void LoopCue_OnClick(object sender, RoutedEventArgs e) =>
        loopTargetCue = LoopCueButton.IsChecked == true ? viewModel.SelectedCue : null;

    private bool ApplyCueLoop(TimeSpan position)
    {
        if (LoopCueButton.IsChecked != true || loopTargetCue is null || !loopTargetCue.TryToCue(out var cue)) return false;
        if (position < cue.End && position >= cue.Start) return false;
        SeekTo(cue.Start); SyncCueToPlayback(cue.Start); return true;
    }

    private void Immersive_OnClick(object sender, RoutedEventArgs e) => SetImmersive(!immersive);
    private void SetImmersive(bool value)
    {
        immersive = value;
        HeaderRow.Height = value ? new GridLength(0) : new GridLength(86);
        FooterRow.Height = value ? new GridLength(0) : (GridLength)FindResource("Size.Footer.Action");
        ListPaneColumn.Width = value ? new GridLength(0) : (GridLength)FindResource("Size.Editor.ListPane");
        InspectorPaneColumn.Width = value ? new GridLength(0) : (GridLength)FindResource("Size.Editor.InspectorPane");
        EditorHeader.Visibility = EditorFooter.Visibility = SubtitleListPane.Visibility = InspectorPane.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        CueTimelinePanel.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(CenterWorkspace, value ? 0 : 1); Grid.SetColumnSpan(CenterWorkspace, value ? 3 : 1);
        VideoFrame.Margin = value ? new Thickness(0) : new Thickness(16, 16, 16, 0);
        PlayerControls.Margin = value ? new Thickness(0) : new Thickness(16, 0, 16, 0);
        VideoFrame.CornerRadius = value ? new CornerRadius(0) : new CornerRadius(9, 9, 0, 0);
        VideoFrame.MaxHeight = value ? double.PositiveInfinity : (double)FindResource("Size.Editor.VideoMaxHeight");
        ImmersiveIcon.Symbol = value ? Wpf.Ui.Controls.SymbolRegular.FullScreenMinimize24 : Wpf.Ui.Controls.SymbolRegular.FullScreenMaximize24;
        ImmersiveButton.ToolTip = value ? "退出沉浸预览（Esc/F）" : "沉浸预览（F）";
        UpdateVideoFrameSize(CenterWorkspace.RenderSize);
    }

    private void Page_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && immersive) { SetImmersive(false); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        switch (e.Key)
        {
            case Key.Space: Play_OnClick(PlayButton, new RoutedEventArgs()); break;
            case Key.Left: Skip(-5); break;
            case Key.Right: Skip(5); break;
            case Key.Up: SelectAdjacentCue(-1); break;
            case Key.Down: SelectAdjacentCue(1); break;
            case Key.M: Mute_OnClick(this, new RoutedEventArgs()); break;
            case Key.L: LoopCueButton.IsChecked = LoopCueButton.IsChecked != true; LoopCue_OnClick(this, new RoutedEventArgs()); break;
            case Key.F: SetImmersive(!immersive); break;
            default: return;
        }
        e.Handled = true;
    }
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
        => UpdateVideoFrameSize(e.NewSize);
    private void UpdateVideoFrameSize(Size size)
    {
        PreviousCuePlayerButton.Visibility = NextCuePlayerButton.Visibility = size.Width >= 520 ? Visibility.Visible : Visibility.Collapsed;
        PlaybackDurationText.Visibility = DurationSeparator.Visibility = size.Width >= 600 ? Visibility.Visible : Visibility.Collapsed;
        SpeedSelector.Visibility = size.Width >= 650 ? Visibility.Visible : Visibility.Collapsed;
        VolumeSlider.Visibility = size.Width >= 720 ? Visibility.Visible : Visibility.Collapsed;
        LoopCueButton.Visibility = size.Width >= 780 ? Visibility.Visible : Visibility.Collapsed;
        var availableHeight = Math.Max(260, size.Height - (immersive ? 88 : 200));
        var maximumHeight = immersive ? double.PositiveInfinity : (double)FindResource("Size.Editor.VideoMaxHeight");
        VideoFrame.Height = Math.Min(maximumHeight, Math.Min(size.Width * 9d / 16d, availableHeight));
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
