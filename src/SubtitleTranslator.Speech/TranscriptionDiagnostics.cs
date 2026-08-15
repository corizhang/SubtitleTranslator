using System.Text.RegularExpressions;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Speech;

public sealed record RepeatedSegmentRun(
    int StartIndex,
    int Count,
    TimeSpan Start,
    TimeSpan End,
    string Text);

public sealed record TranscriptionDiagnostics(
    IReadOnlyList<RepeatedSegmentRun> RepeatedRuns)
{
    public int RepeatedSegmentCount => RepeatedRuns.Sum(run => run.Count);
    public bool HasSuspiciousRepeats => RepeatedRuns.Count > 0;
}

public static partial class TranscriptionDiagnosticsAnalyzer
{
    public static TranscriptionDiagnostics Analyze(
        IReadOnlyList<TranscriptSegment> segments,
        int minimumRunLength = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumRunLength, 2);
        var runs = new List<RepeatedSegmentRun>();

        for (var start = 0; start < segments.Count;)
        {
            var normalized = Normalize(segments[start].Text);
            var end = start + 1;
            while (end < segments.Count && Normalize(segments[end].Text) == normalized)
                end++;

            if (normalized.Length > 0 && end - start >= minimumRunLength)
            {
                runs.Add(new RepeatedSegmentRun(
                    start,
                    end - start,
                    segments[start].Start,
                    segments[end - 1].End,
                    segments[start].Text));
            }

            start = end;
        }

        return new TranscriptionDiagnostics(runs);
    }

    private static string Normalize(string text) =>
        WhitespaceRegex().Replace(text.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();
}

