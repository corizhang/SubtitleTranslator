using System.Diagnostics;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Speech;

public sealed class RetryingTranscriptionEngine(
    ITranscriptionEngine primary,
    ITranscriptionEngine retryEngine,
    IAudioRegionExtractor regionExtractor,
    string retryDirectory,
    TimeSpan maximumRetryWindow,
    TimeSpan retryPadding) : ITranscriptionEngine
{
    public async Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var initial = await primary.TranscribeAsync(audio, options, progress, cancellationToken);
        var current = initial.Segments;
        var initialDiagnostics = TranscriptionDiagnosticsAnalyzer.Analyze(current);
        var mediaDuration = audio.Duration ?? current.LastOrDefault()?.End ?? TimeSpan.Zero;
        var windows = RetryWindowPlanner.Plan(
            initialDiagnostics, mediaDuration, maximumRetryWindow, retryPadding);

        if (windows.Count == 0)
            return initial;

        Directory.CreateDirectory(retryDirectory);
        for (var index = 0; index < windows.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var window = windows[index];
            progress?.Report(new PipelineProgress(
                "retry", index * 100d / windows.Count,
                $"Retrying suspicious window {index + 1}/{windows.Count}"));

            var path = Path.Combine(retryDirectory, $"retry-{index:0000}.wav");
            var extracted = await regionExtractor.ExtractAsync(
                audio, window.Start, window.End - window.Start, path, cancellationToken);
            var retried = await retryEngine.TranscribeAsync(
                extracted,
                options with { NoContext = true },
                progress,
                cancellationToken);
            var candidate = TranscriptionResultRepairer.ReplaceWindow(
                current, window.Start, window.End, retried.Segments);

            var before = CountSuspiciousSegmentsInWindow(current, window.Start, window.End);
            var after = CountSuspiciousSegmentsInWindow(candidate, window.Start, window.End);
            if (after < before)
            {
                current = candidate;
                progress?.Report(new PipelineProgress(
                    "retry", (index + 1) * 100d / windows.Count,
                    $"Accepted retry {index + 1}: suspicious segments {before} -> {after}"));
            }
            else
            {
                progress?.Report(new PipelineProgress(
                    "retry", (index + 1) * 100d / windows.Count,
                    $"Rejected retry {index + 1}: no diagnostic improvement"));
            }
        }

        stopwatch.Stop();
        return initial with
        {
            Engine = $"{initial.Engine} + diagnostic retry",
            ProcessingTime = stopwatch.Elapsed,
            Segments = current
        };
    }

    private static int CountSuspiciousSegmentsInWindow(
        IReadOnlyList<TranscriptSegment> segments,
        TimeSpan start,
        TimeSpan end) => TranscriptionDiagnosticsAnalyzer.Analyze(segments).RepeatedRuns
        .Where(run => run.End > start && run.Start < end)
        .Sum(run => run.Count);
}
