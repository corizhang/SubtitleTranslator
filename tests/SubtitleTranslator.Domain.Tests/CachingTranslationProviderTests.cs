using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Domain.Tests;

public sealed class CachingTranslationProviderTests
{
    [Fact]
    public async Task TranslateAsync_ReusesValidatedDiskCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"translation-cache-{Guid.NewGuid():N}");
        try
        {
            var inner = new CountingProvider();
            var provider = new CachingTranslationProvider(inner, directory, "test-v1");
            var batch = new TranslationBatch([new TranslationRequestSegment(4, "Hello")], "en");

            var first = await provider.TranslateAsync(batch, new TranslationContext(), CancellationToken.None);
            var second = await provider.TranslateAsync(batch, new TranslationContext(), CancellationToken.None);

            Assert.Equal(1, inner.Calls);
            Assert.Equal("译文:Hello", Assert.Single(first).Text);
            Assert.Equal(first, second);
            Assert.Single(Directory.GetFiles(directory, "*.json"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CountingProvider : ITranslationProvider
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
            TranslationBatch batch, TranslationContext context, CancellationToken cancellationToken)
        {
            Calls++;
            IReadOnlyList<TranslationSegment> result = batch.Segments
                .Select(item => new TranslationSegment(item.SegmentId, $"译文:{item.Text}"))
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
