using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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

                Assert.True(all.ActualHeight >= 38, $"Filter height failed at {scale:P0}.");
                Assert.InRange(Math.Abs(all.ActualWidth - error.ActualWidth), 0, 0.5);
                Assert.InRange(Math.Abs(error.ActualWidth - suggestion.ActualWidth), 0, 0.5);
                Assert.InRange(Math.Abs(suggestion.ActualWidth - modified.ActualWidth), 0, 0.5);
                Assert.True(center.ActualWidth >= 700, $"Editor center too narrow at {scale:P0}.");
                Assert.True(footer.ActualHeight >= 60, $"Footer clipped at {scale:P0}.");
            }
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
