using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class TranslationReviewOrchestrator(ITranslationReviewProvider provider)
{
    public async Task<IReadOnlyList<TranslationSegment>> ReviewAsync(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<TranslationSegment> translations,
        TranslationContext context,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = TranslationQualityAnalyzer.FindCandidates(transcript, translations);
        if (candidates.Count == 0)
            return translations;

        progress?.Report(new PipelineProgress("translation-qa", 0, $"Reviewing {candidates.Count} candidate segments"));
        var response = await provider.ReviewAsync(candidates, context, cancellationToken);
        var duplicate = response.GroupBy(item => item.SegmentId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Translation review contains duplicate SegmentId {duplicate.Key}.");
        var candidateIds = candidates.Select(item => item.SegmentId).ToHashSet();
        var unknown = response.FirstOrDefault(item => !candidateIds.Contains(item.SegmentId));
        if (unknown is not null)
            throw new InvalidOperationException($"Translation review contains unknown SegmentId {unknown.SegmentId}.");
        var byId = response.ToDictionary(item => item.SegmentId);
        var missing = candidates.FirstOrDefault(item => !byId.ContainsKey(item.SegmentId));
        if (missing is not null)
            throw new InvalidOperationException($"Translation review is missing SegmentId {missing.SegmentId}.");
        var empty = response.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Text));
        if (empty is not null)
            throw new InvalidOperationException($"Translation review has empty text for SegmentId {empty.SegmentId}.");

        var replacements = response.Where(item => item.Changed)
            .ToDictionary(item => item.SegmentId, item => item.Text.Trim());
        var final = translations.Select(item => replacements.TryGetValue(item.SegmentId, out var text)
            ? item with { Text = text }
            : item).ToArray();
        progress?.Report(new PipelineProgress(
            "translation-qa", 100, $"Reviewed {candidates.Count}; changed {replacements.Count}"));
        return final;
    }
}
