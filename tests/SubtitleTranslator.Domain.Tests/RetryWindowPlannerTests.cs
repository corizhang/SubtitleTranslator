using SubtitleTranslator.Speech;

namespace SubtitleTranslator.Domain.Tests;

public sealed class RetryWindowPlannerTests
{
    [Fact]
    public void Plan_SplitsLongRepeatIntoBoundedWindows()
    {
        var diagnostics = new TranscriptionDiagnostics([
            new RepeatedSegmentRun(
                10,
                20,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                "repeat")
        ]);

        var windows = RetryWindowPlanner.Plan(
            diagnostics,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(10));

        Assert.Equal(3, windows.Count);
        Assert.All(windows, window => Assert.True(window.End - window.Start <= TimeSpan.FromMinutes(2)));
        Assert.Equal(TimeSpan.FromSeconds(50), windows[0].Start);
        Assert.Equal(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10), windows[^1].End);
    }

    [Fact]
    public void Plan_MergesPartiallyOverlappingWindows()
    {
        var diagnostics = new TranscriptionDiagnostics([
            new RepeatedSegmentRun(1, 3, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(30), "a"),
            new RepeatedSegmentRun(5, 3, TimeSpan.FromSeconds(35), TimeSpan.FromSeconds(45), "b")
        ]);

        var windows = RetryWindowPlanner.Plan(
            diagnostics,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(10));

        var window = Assert.Single(windows);
        Assert.Equal(TimeSpan.FromSeconds(10), window.Start);
        Assert.Equal(TimeSpan.FromSeconds(55), window.End);
    }
}
