using SubtitleTranslator.Domain;
using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Domain.Tests;

public sealed class TranslationQualityAnalyzerTests
{
    [Fact]
    public void FindCandidates_FlagsGoneNearCasualtyContext()
    {
        TranscriptSegment[] transcript =
        [
            new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Are you sure?"),
            new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "They're gone."),
            new(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Two agents down, another captured.")
        ];
        TranslationSegment[] translations =
        [new(0, "你确定？"), new(1, "他们不见了。"), new(2, "两名特工殉职，另一名被俘。")];

        var candidates = TranslationQualityAnalyzer.FindCandidates(transcript, translations);

        var gone = Assert.Single(candidates, item => item.SegmentId == 1);
        Assert.Contains("euphemism", gone.Reason);
        Assert.Equal(3, gone.Context.Count);
    }

    [Fact]
    public async Task DeepSeekReviewProvider_PreservesExistingDialogueMarker()
    {
        var candidate = new TranslationReviewCandidate(
            6, "Are you sure?", "- 你确定？", "short context-dependent expression",
            [new TranslationReviewContextLine(6, "Are you sure?", "- 你确定？", true)]);
        var provider = new DeepSeekTranslationReviewProvider(new MarkerDroppingProvider());

        var result = await provider.ReviewAsync([candidate], new TranslationContext(), CancellationToken.None);

        Assert.Equal("- 你确定？", Assert.Single(result).Text);
        Assert.False(Assert.Single(result).Changed);
    }

    [Fact]
    public async Task ReviewOrchestrator_AppliesOnlyChangedValidatedResults()
    {
        TranscriptSegment[] transcript =
        [
            new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Are you sure?"),
            new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "They're gone."),
            new(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), "Two agents down.")
        ];
        TranslationSegment[] translations =
        [new(0, "你确定？"), new(1, "他们不见了。"), new(2, "两名特工殉职。")];
        var provider = new StubReviewProvider();

        var result = await new TranslationReviewOrchestrator(provider).ReviewAsync(
            transcript, translations, new TranslationContext(), null, CancellationToken.None);

        Assert.Equal("他们没了。", result.Single(item => item.SegmentId == 1).Text);
        Assert.Equal("你确定？", result.Single(item => item.SegmentId == 0).Text);
    }

    private sealed class StubReviewProvider : SubtitleTranslator.Application.ITranslationReviewProvider
    {
        public Task<IReadOnlyList<TranslationReviewResult>> ReviewAsync(
            IReadOnlyList<TranslationReviewCandidate> candidates,
            TranslationContext context,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<TranslationReviewResult> result = candidates.Select(item =>
                item.SegmentId == 1
                    ? new TranslationReviewResult(item.SegmentId, "他们没了。", true)
                    : new TranslationReviewResult(item.SegmentId, item.CurrentTranslation, false)).ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class MarkerDroppingProvider : SubtitleTranslator.Application.ITranslationProvider
    {
        public Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
            TranslationBatch batch, TranslationContext context, CancellationToken cancellationToken)
        {
            IReadOnlyList<TranslationSegment> result = [new(batch.Segments[0].SegmentId, "你确定？")];
            return Task.FromResult(result);
        }
    }
}
