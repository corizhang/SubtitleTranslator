using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Speech;

public static class TranscriptionResultRepairer
{
    public static IReadOnlyList<TranscriptSegment> ReplaceWindow(
        IReadOnlyList<TranscriptSegment> original,
        TimeSpan windowStart,
        TimeSpan windowEnd,
        IReadOnlyList<TranscriptSegment> relativeReplacement)
    {
        if (windowEnd <= windowStart)
            throw new ArgumentOutOfRangeException(nameof(windowEnd));

        static TimeSpan Midpoint(TranscriptSegment segment) =>
            segment.Start + TimeSpan.FromTicks((segment.End - segment.Start).Ticks / 2);

        var kept = original.Where(segment =>
        {
            var midpoint = Midpoint(segment);
            return midpoint < windowStart || midpoint >= windowEnd;
        });
        var replacement = relativeReplacement
            .Where(segment => segment.End > segment.Start && !string.IsNullOrWhiteSpace(segment.Text))
            .Select(segment => segment with
            {
                Start = windowStart + segment.Start,
                End = windowStart + segment.End,
                Text = segment.Text.Trim()
            })
            .Where(segment =>
            {
                var midpoint = Midpoint(segment);
                return midpoint >= windowStart && midpoint < windowEnd;
            });

        return kept.Concat(replacement)
            .OrderBy(segment => segment.Start)
            .ThenBy(segment => segment.End)
            .Select((segment, index) => segment with { Index = index })
            .ToArray();
    }
}
