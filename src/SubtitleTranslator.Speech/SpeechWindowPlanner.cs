using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Speech;

public static class SpeechWindowPlanner
{
    public static IReadOnlyList<SpeechWindow> Plan(
        IReadOnlyList<SpeechRegion> regions,
        TimeSpan maximumGap,
        TimeSpan maximumDuration)
    {
        if (maximumGap < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumGap));
        if (maximumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumDuration));
        if (regions.Count == 0)
            return [];

        var ordered = regions.OrderBy(region => region.Start).ToArray();
        var groups = new List<List<SpeechRegion>>();
        var current = new List<SpeechRegion> { ordered[0] };

        foreach (var region in ordered.Skip(1))
        {
            var proposedDuration = region.End - current[0].Start;
            var gap = region.Start - current[^1].End;
            if (gap <= maximumGap && proposedDuration <= maximumDuration)
                current.Add(region);
            else
            {
                groups.Add(current);
                current = [region];
            }
        }
        groups.Add(current);

        return groups.Select((group, index) => new SpeechWindow(
            group[0].Start,
            group[^1].End,
            group,
            index,
            groups.Count)).ToArray();
    }
}

