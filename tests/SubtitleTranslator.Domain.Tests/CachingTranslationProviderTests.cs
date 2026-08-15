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

    [Fact]
    public async Task Orchestrator_RepairsMissingSegment_AndCachesMergedBatch()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"translation-cache-{Guid.NewGuid():N}");
        try
        {
            var inner = new OmitsLastSegmentFromMultiItemBatchProvider();
            var cached = new CachingTranslationProvider(inner, directory, "test-v1");
            var orchestrator = new TranslationOrchestrator(cached);
            var transcript = new[]
            {
                new TranscriptSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello"),
                new TranscriptSegment(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "World")
            };
            var options = new TranslationOptions(MaximumAttemptsPerBatch: 2);

            var first = await orchestrator.TranslateAsync(
                transcript, "en", new TranslationContext(), options, null, CancellationToken.None);
            var second = await orchestrator.TranslateAsync(
                transcript, "en", new TranslationContext(), options, null, CancellationToken.None);

            Assert.Equal(2, inner.Calls);
            Assert.Equal([[0, 1], [1]], inner.RequestedIds);
            Assert.Equal(first, second);
            Assert.Equal(2, first.Count);
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

    private sealed class OmitsLastSegmentFromMultiItemBatchProvider : ITranslationProvider
    {
        public int Calls { get; private set; }
        public List<int[]> RequestedIds { get; } = [];

        public Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
            TranslationBatch batch, TranslationContext context, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedIds.Add(batch.Segments.Select(x => x.SegmentId).ToArray());
            var source = batch.Segments.Count > 1 ? batch.Segments.Take(batch.Segments.Count - 1) : batch.Segments;
            IReadOnlyList<TranslationSegment> result = source
                .Select(item => new TranslationSegment(item.SegmentId, $"译文:{item.Text}"))
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
