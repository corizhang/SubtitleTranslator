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
