using SubtitleTranslator.Domain;
using SubtitleTranslator.Speech;

namespace SubtitleTranslator.Domain.Tests;

public sealed class TranscriptionDiagnosticsTests
{
    [Fact]
    public void Analyze_FindsConsecutiveNormalizedRepeats()
    {
        var segments = new[]
        {
            Segment(0, "Hello world"),
            Segment(1, " hello   WORLD "),
            Segment(2, "Hello world"),
            Segment(3, "Different")
        };

        var result = TranscriptionDiagnosticsAnalyzer.Analyze(segments);

        var run = Assert.Single(result.RepeatedRuns);
        Assert.Equal(3, run.Count);
        Assert.Equal(3, result.RepeatedSegmentCount);
    }

    private static TranscriptSegment Segment(int index, string text) => new(
        index,
        TimeSpan.FromSeconds(index),
        TimeSpan.FromSeconds(index + 1),
        text);
}
