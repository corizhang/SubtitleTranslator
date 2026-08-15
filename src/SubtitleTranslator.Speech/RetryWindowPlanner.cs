namespace SubtitleTranslator.Speech;

public sealed record TranscriptionRetryWindow(
    TimeSpan Start,
    TimeSpan End,
    string Reason,
    int SourceRunStartIndex,
    int SourceRunCount);

public static class RetryWindowPlanner
{
    public static IReadOnlyList<TranscriptionRetryWindow> Plan(
        TranscriptionDiagnostics diagnostics,
        TimeSpan mediaDuration,
        TimeSpan maximumWindow,
        TimeSpan padding)
    {
        if (maximumWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumWindow));
        if (padding < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(padding));

        var windows = new List<TranscriptionRetryWindow>();
        foreach (var run in diagnostics.RepeatedRuns)
        {
            var affectedStart = Max(TimeSpan.Zero, run.Start - padding);
            var affectedEnd = Min(mediaDuration, run.End + padding);
            for (var start = affectedStart; start < affectedEnd; start += maximumWindow)
            {
                windows.Add(new TranscriptionRetryWindow(
                    start,
                    Min(affectedEnd, start + maximumWindow),
                    $"{run.Count} consecutive repeated segments",
                    run.StartIndex,
                    run.Count));
            }
        }

        return MergeOverlaps(windows, maximumWindow);
    }

    private static IReadOnlyList<TranscriptionRetryWindow> MergeOverlaps(
        IReadOnlyList<TranscriptionRetryWindow> windows,
        TimeSpan maximumWindow)
    {
        if (windows.Count < 2)
            return windows;

        var ordered = windows.OrderBy(window => window.Start).ThenBy(window => window.End).ToArray();
        var merged = new List<TranscriptionRetryWindow> { ordered[0] };
        for (var index = 1; index < ordered.Length; index++)
        {
            var current = ordered[index];
            var previous = merged[^1];
            if (current.Start < previous.End &&
                Max(previous.End, current.End) - previous.Start <= maximumWindow)
            {
                merged[^1] = previous with
                {
                    End = current.End > previous.End ? current.End : previous.End,
                    Reason = $"{previous.Reason}; {current.Reason}"
                };
            }
            else
            {
                merged.Add(current);
            }
        }
        return merged;
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}
