using SubtitleTranslator.Domain;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.Subtitles.Tests;

public sealed class TranslatedSrtExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesOriginalThenChinese()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-{Guid.NewGuid():N}.srt");
        try
        {
            TranscriptSegment[] transcript =
            [
                new(5, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2.5), "Hello")
            ];
            TranslationSegment[] translations = [new(5, "你好")];

            await new TranslatedSrtExporter().ExportAsync(
                transcript, translations, TranslatedSubtitleLayout.OriginalThenChinese,
                path, CancellationToken.None);

            var text = await File.ReadAllTextAsync(path);
            Assert.Contains("Hello\r\n你好", text.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
            Assert.Contains("00:00:01,000 --> 00:00:02,500", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
