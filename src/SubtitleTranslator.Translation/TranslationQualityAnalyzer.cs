using System.Text.RegularExpressions;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public static partial class TranslationQualityAnalyzer
{
    private static readonly HashSet<string> AmbiguousShortExpressions = new(StringComparer.OrdinalIgnoreCase)
    {
        "it", "this", "that", "they", "them", "he", "she", "gone", "right", "fine",
        "okay", "ok", "sure", "really", "what", "why", "yes", "no"
    };

    public static IReadOnlyList<TranslationReviewCandidate> FindCandidates(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<TranslationSegment> translations,
        int contextRadius = 2)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(contextRadius);
        var byId = TranslationResponseValidator.ValidateAndOrder(
            transcript.Select(item => new TranslationRequestSegment(item.Index, item.Text)).ToArray(),
            translations).ToDictionary(item => item.SegmentId);
        var result = new List<TranslationReviewCandidate>();

        for (var index = 0; index < transcript.Count; index++)
        {
            var segment = transcript[index];
            var words = WordRegex().Matches(segment.Text).Select(match => match.Value).ToArray();
            var reasons = new List<string>();
            if (words.Length <= 4 && words.Any(word => AmbiguousShortExpressions.Contains(word)))
                reasons.Add("short context-dependent expression");
            if (words.Any(word => string.Equals(word, "gone", StringComparison.OrdinalIgnoreCase)) &&
                HasNearbyMortalityCue(transcript, index, contextRadius))
                reasons.Add("possible euphemism conflicts with nearby casualty context");
            if (reasons.Count == 0)
                continue;

            var start = Math.Max(0, index - contextRadius);
            var end = Math.Min(transcript.Count - 1, index + contextRadius);
            var lines = Enumerable.Range(start, end - start + 1)
                .Select(position => new TranslationReviewContextLine(
                    transcript[position].Index,
                    transcript[position].Text,
                    byId[transcript[position].Index].Text,
                    position == index))
                .ToArray();
            result.Add(new TranslationReviewCandidate(
                segment.Index, segment.Text, byId[segment.Index].Text,
                string.Join("; ", reasons), lines));
        }
        return result;
    }

    private static bool HasNearbyMortalityCue(
        IReadOnlyList<TranscriptSegment> transcript, int index, int radius)
    {
        var start = Math.Max(0, index - radius);
        var end = Math.Min(transcript.Count - 1, index + radius);
        var context = string.Join(' ', Enumerable.Range(start, end - start + 1)
            .Select(position => transcript[position].Text)).ToLowerInvariant();
        return MortalityCueRegex().IsMatch(context);
    }

    [GeneratedRegex("[\\p{L}']+")]
    private static partial Regex WordRegex();

    [GeneratedRegex("\\b(dead|died|death|killed|casualt(?:y|ies)|agents? down|didn't make it|lost them)\\b")]
    private static partial Regex MortalityCueRegex();
}
