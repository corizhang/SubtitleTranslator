using SubtitleTranslator.Domain;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.Subtitles.Tests;

public sealed class SrtExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesUtf8SrtWithValidTimestamps()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-{Guid.NewGuid():N}.srt");
        try
        {
            var segments = new[]
            {
                new TranscriptSegment(0, TimeSpan.FromMilliseconds(1234), TimeSpan.FromMilliseconds(3567), "你好，世界")
            };

            await new SrtExporter().ExportAsync(segments, path, CancellationToken.None);
            var content = await File.ReadAllTextAsync(path);

            Assert.Contains("00:00:01,234 --> 00:00:03,567", content);
            Assert.Contains("你好，世界", content);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
