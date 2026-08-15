using SubtitleTranslator.Domain;
using SubtitleTranslator.Speech;

namespace SubtitleTranslator.Domain.Tests;

public sealed class TranscriptionResultRepairerTests
{
    [Fact]
    public void ReplaceWindow_RestoresAbsoluteTimeAndReindexes()
    {
        TranscriptSegment[] original =
        [
            new(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "before"),
            new(1, TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(12), "bad"),
            new(2, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(22), "after")
        ];
        TranscriptSegment[] replacement =
        [
            new(0, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), " fixed ")
        ];

        var result = TranscriptionResultRepairer.ReplaceWindow(
            original, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), replacement);

        Assert.Equal([0, 1, 2], result.Select(segment => segment.Index));
        Assert.Equal("fixed", result[1].Text);
        Assert.Equal(TimeSpan.FromSeconds(12), result[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(14), result[1].End);
    }
}
