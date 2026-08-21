using System.Runtime.ExceptionServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;

namespace SubtitleTranslator.App.UiTests;

public sealed class DpiLayoutRegressionTests
{
    [Fact]
    public void SubtitleEditor_RemainsUsable_AtSupportedDpiScales()
    {
        RunOnSta(() =>
        {
            var app = new App();
            app.InitializeComponent();

            foreach (var scale in new[] { 1d, 1.25d, 1.5d })
            {
                var physicalWidth = 2560d;
                var physicalHeight = 1400d;
                var windowWidth = physicalWidth / scale;
                var windowHeight = physicalHeight / scale;
                var editorWidth = windowWidth - 220d;
                var editorHeight = windowHeight - 48d;
                var page = new SubtitleEditorPage("missing.srt", "missing.mkv", null, () => { });

                page.Measure(new Size(editorWidth, editorHeight));
                page.Arrange(new Rect(0, 0, editorWidth, editorHeight));
                page.UpdateLayout();

                var all = Assert.IsType<ToggleButton>(page.FindName("AllFilterButton"));
                var error = Assert.IsType<ToggleButton>(page.FindName("ErrorFilterButton"));
                var suggestion = Assert.IsType<ToggleButton>(page.FindName("SuggestionFilterButton"));
                var modified = Assert.IsType<ToggleButton>(page.FindName("ModifiedFilterButton"));
                var center = Assert.IsType<Grid>(page.FindName("CenterWorkspace"));
                var footer = Assert.IsType<Border>(page.FindName("EditorFooter"));
                var playerControls = Assert.IsType<Border>(page.FindName("PlayerControls"));
                var videoFrame = Assert.IsType<Border>(page.FindName("VideoFrame"));
                var playbackSlider = Assert.IsType<Slider>(page.FindName("PlaybackSlider"));

                Assert.True(all.ActualHeight >= 38, $"Filter height failed at {scale:P0}.");
                Assert.InRange(Math.Abs(all.ActualWidth - error.ActualWidth), 0, 0.5);
                Assert.InRange(Math.Abs(error.ActualWidth - suggestion.ActualWidth), 0, 0.5);
                Assert.InRange(Math.Abs(suggestion.ActualWidth - modified.ActualWidth), 0, 0.5);
                Assert.True(center.ActualWidth >= 700, $"Editor center too narrow at {scale:P0}.");
                Assert.True(footer.ActualHeight >= 60, $"Footer clipped at {scale:P0}.");
                Assert.True(playerControls.ActualHeight >= 80, $"Player controls clipped at {scale:P0}.");
                Assert.True(playbackSlider.ActualWidth >= 500, $"Player seek bar too narrow at {scale:P0}.");
                Assert.InRange(Math.Abs(videoFrame.ActualWidth - playerControls.ActualWidth), 0, 0.5);
                Assert.InRange(videoFrame.ActualWidth / videoFrame.ActualHeight, 1.776, 1.779);

                var immersiveButton = Assert.IsType<Button>(page.FindName("ImmersiveButton"));
                var listPane = Assert.IsType<Border>(page.FindName("SubtitleListPane"));
                var inspector = Assert.IsType<Border>(page.FindName("InspectorPane"));
                immersiveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); page.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, listPane.Visibility);
                Assert.Equal(Visibility.Collapsed, inspector.Visibility);
                Assert.Equal(3, Grid.GetColumnSpan(center));
                Assert.Equal(0, Grid.GetColumn(center));
            }

            var compactPage = new SubtitleEditorPage("missing.srt", "missing.mkv", null, () => { });
            compactPage.Measure(new Size(1146, 720)); compactPage.Arrange(new Rect(0, 0, 1146, 720)); compactPage.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, Assert.IsType<Slider>(compactPage.FindName("VolumeSlider")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<ComboBox>(compactPage.FindName("SpeedSelector")).Visibility);
            Assert.Equal(Visibility.Collapsed, Assert.IsType<ToggleButton>(compactPage.FindName("LoopCueButton")).Visibility);
            Assert.Equal(Visibility.Visible, Assert.IsType<Button>(compactPage.FindName("PlayButton")).Visibility);

            var dashboard = new WorkbenchDashboard();
            dashboard.Measure(new Size(1060, 772)); dashboard.Arrange(new Rect(0, 0, 1060, 772)); dashboard.UpdateLayout();
            var dropArea = Assert.IsType<Border>(dashboard.FindName("DropArea"));
            var recentTaskCard = Assert.IsType<Border>(dashboard.FindName("RecentTaskCard"));
            var environmentCard = Assert.IsType<Border>(dashboard.FindName("EnvironmentCard"));
            Assert.True(dropArea.ActualHeight >= 280, "Workbench drop area no longer matches the primary task hierarchy.");
            Assert.True(recentTaskCard.ActualWidth > environmentCard.ActualWidth * 1.7, "Workbench column proportions regressed.");
            Assert.True(environmentCard.ActualWidth >= 280, "Environment rows are too narrow.");

            dashboard.Measure(new Size(1500, 1200)); dashboard.Arrange(new Rect(0, 0, 1500, 1200)); dashboard.UpdateLayout();
            var newTaskCard = Assert.IsType<Border>(dashboard.FindName("NewTaskCard"));
            var storageCard = Assert.IsType<Border>(dashboard.FindName("StorageCard"));
            Assert.True(recentTaskCard.ActualHeight >= 430, "Recent tasks should consume remaining maximized height.");
            Assert.True(storageCard.ActualHeight >= 260, "Storage card should consume remaining maximized height.");
            Assert.True(recentTaskCard.ActualHeight > newTaskCard.ActualHeight, "Maximized workbench still leaves unused space below recent tasks.");
        });
    }

    [Fact]
    public void SubtitleEditor_FindsCueForPlaybackPosition_AndHonorsGaps()
    {
        RunOnSta(() =>
        {
            var viewModel = new SubtitleEditorViewModel();
            var first = new EditableSubtitleCue(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "first");
            var second = new EditableSubtitleCue(2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "second");
            viewModel.Cues.Add(first); viewModel.Cues.Add(second);

            Assert.Same(first, viewModel.FindCueAt(TimeSpan.FromSeconds(1.5)));
            Assert.Null(viewModel.FindCueAt(TimeSpan.FromSeconds(2.5)));
            Assert.Same(second, viewModel.FindCueAt(TimeSpan.FromSeconds(3)));
            Assert.Null(viewModel.FindCueAt(TimeSpan.FromSeconds(4)));
        });
    }

    [Theory]
    [InlineData(4d / 3d)]
    [InlineData(21d / 9d)]
    [InlineData(9d / 16d)]
    public void SubtitleEditor_FitsNativeVideoAspectRatios(double aspectRatio)
    {
        var size = SubtitleEditorPage.CalculateVideoFrameSize(aspectRatio, 1200, 640, 640);
        Assert.InRange(size.Width / size.Height, aspectRatio - 0.001, aspectRatio + 0.001);
        Assert.True(size.Width <= 1200);
        Assert.True(size.Height <= 640);
    }

    [Fact]
    public void SubtitleEditor_HonorsPixelAspectRatio_AndRotationMetadata()
    {
        var rotated = SubtitleEditorPage.CalculateDisplayAspectRatio(1920, 1080, 1, 1, VideoOrientation.RightTop);
        Assert.InRange(rotated, 0.561, 0.564);
        var anamorphic = SubtitleEditorPage.CalculateDisplayAspectRatio(720, 576, 16, 15, VideoOrientation.TopLeft);
        Assert.InRange(anamorphic, 1.332, 1.334);
    }

    [Theory]
    [InlineData("已完成", "查看")]
    [InlineData("处理中", "查看进度")]
    [InlineData("失败，可恢复", "重试")]
    [InlineData("已取消，可恢复", "重新开始")]
    [InlineData("可继续", "继续")]
    public void RecentProject_UsesStatusSpecificAction(string status, string expected)
    {
        var project = new ProjectHistoryItem("project", "name", "missing.mkv", status, 0, DateTime.UtcNow, 0, [], []);
        Assert.Equal(expected, project.ActionText);
    }

    [Fact]
    public void ProjectLibrary_UsesMediaFactsAndCompletionSpecificAction()
    {
        var project = new ProjectHistoryItem("project", "name", "missing.mkv", "已完成", 100, DateTime.UtcNow, 0, [], [])
        {
            MediaDetails = "1920×1080丨42:18丨H.264"
        };

        Assert.Equal("1920×1080", project.ResolutionDisplay);
        Assert.Equal("42:18", project.DurationDisplay);
        Assert.Equal("校订字幕", project.LibraryPrimaryActionText);
    }

    [Fact]
    public void ProjectLibrary_SummaryIsExposedAsOneReadOnlyDisplayValue()
    {
        var viewModel = new ProjectHistoryViewModel();
        Assert.Equal("共 0 个项目  ·  0 个已完成  ·  占用 0 KB", viewModel.ProjectSummaryDisplay);
    }

    [Fact]
    public void ProjectLibrary_ThumbnailFallsBackWhenCacheDoesNotExist()
    {
        var project = new ProjectHistoryItem("project", "name", "missing.mkv", "已完成", 100, DateTime.UtcNow, 0, [], [])
        {
            ThumbnailPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jpg")
        };
        Assert.False(project.HasThumbnail);
    }

    [Fact]
    public void BatchCompletionPointsToProjectLibraryInsteadOfActingAsPermanentHistory()
    {
        var item = new BatchQueueItemViewModel(new SubtitleTranslator.Application.BatchQueueEntry(
            Guid.NewGuid(), "missing.mkv", SubtitleTranslator.Application.BatchTaskState.Completed,
            100, "字幕已生成", null, "missing.srt", DateTime.UtcNow));

        Assert.Equal("已完成", item.StateDisplay);
        Assert.Equal("查看项目", item.PrimaryActionText);
        Assert.Contains("项目库", item.OutcomeDisplay);
    }

    [Fact]
    public void WorkbenchActivityPrioritizesRecoverableWorkOverCompletedProjects()
    {
        var failed = new ProjectHistoryItem("failed", "failed", "missing.mkv", "失败，可恢复", 40, DateTime.UtcNow, 0, [], []);
        var completed = new ProjectHistoryItem("completed", "completed", "missing.mkv", "已完成", 100, DateTime.UtcNow, 0, [], []);

        Assert.True(failed.ActivityPriority < completed.ActivityPriority);
        Assert.Equal("需要处理 · 可恢复", failed.ActivityDisplay);
        Assert.Equal("校订", completed.WorkbenchActionText);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
