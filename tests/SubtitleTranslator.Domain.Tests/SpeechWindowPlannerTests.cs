using SubtitleTranslator.Domain;
using SubtitleTranslator.Speech;

namespace SubtitleTranslator.Domain.Tests;

public sealed class SpeechWindowPlannerTests
{
    [Fact]
    public void Plan_MergesNearbyRegionsAndSplitsLargeGaps()
    {
        var regions = new[]
        {
            new SpeechRegion(TimeSpan.Zero, TimeSpan.FromSeconds(2)),
            new SpeechRegion(TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6)),
            new SpeechRegion(TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14))
        };

        var windows = SpeechWindowPlanner.Plan(regions, TimeSpan.FromSeconds(3), TimeSpan.FromMinutes(5));

        Assert.Equal(2, windows.Count);
        Assert.Equal(2, windows[0].Regions.Count);
        Assert.Equal(TimeSpan.FromSeconds(12), windows[1].Start);
    }
}
