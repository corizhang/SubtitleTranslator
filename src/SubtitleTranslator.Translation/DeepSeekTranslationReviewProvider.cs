using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class DeepSeekTranslationReviewProvider(ITranslationProvider translationProvider)
    : ITranslationReviewProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TranslationReviewResult>> ReviewAsync(
        IReadOnlyList<TranslationReviewCandidate> candidates,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var requests = candidates.Select(candidate => new TranslationRequestSegment(
            candidate.SegmentId,
            BuildReviewInstruction(candidate))).ToArray();
        var response = await translationProvider.TranslateAsync(
            new TranslationBatch(requests, "multilingual subtitle QA", "zh-CN"),
            context with
            {
                Style = "Subtitle QA. Return only the corrected Chinese translation for each target; keep the current translation when it is already contextually correct."
            },
            cancellationToken);
        var ordered = TranslationResponseValidator.ValidateAndOrder(requests, response);
        var currentById = candidates.ToDictionary(item => item.SegmentId, item => item.CurrentTranslation.Trim());
        return ordered.Select(item =>
        {
            var current = currentById[item.SegmentId];
            var reviewed = PreserveDialogueMarker(current, item.Text.Trim());
            return new TranslationReviewResult(
                item.SegmentId,
                reviewed,
                !string.Equals(reviewed, current, StringComparison.Ordinal),
                "DeepSeek contextual subtitle QA");
        }).ToArray();
    }

    private static string BuildReviewInstruction(TranslationReviewCandidate candidate) => $$"""
        Review only the TARGET subtitle in this continuous scene.
        Resolve ambiguity using both earlier and later context. Correct literal mistranslation, pronouns,
        ellipsis, euphemisms, tone, and continuity. Output concise natural Chinese for the TARGET only.
        Reason flagged by local analyzer: {{candidate.Reason}}
        Current target translation: {{candidate.CurrentTranslation}}
        Scene JSON: {{JsonSerializer.Serialize(candidate.Context, JsonOptions)}}
        """;

    private static string PreserveDialogueMarker(string current, string reviewed)
    {
        if (current.StartsWith("- ", StringComparison.Ordinal) &&
            !reviewed.StartsWith("- ", StringComparison.Ordinal))
            return $"- {reviewed.TrimStart('-', ' ')}";
        return reviewed;
    }
}
