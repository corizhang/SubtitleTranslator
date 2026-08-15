using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class PrefixTestTranslationProvider(string prefix = "【测试译文】") : ITranslationProvider
{
    public Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<TranslationSegment> result = batch.Segments
            .Select(item => new TranslationSegment(item.SegmentId, $"{prefix}{item.Text}"))
            .ToArray();
        return Task.FromResult(result);
    }
}
