using System.Diagnostics;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using Whisper.net;

namespace SubtitleTranslator.Speech;

public sealed class VadWhisperNetTranscriptionEngine(
    IVoiceActivityDetector detector,
    IAudioRegionExtractor regionExtractor,
    VoiceActivityOptions vadOptions,
    TimeSpan maximumGap,
    TimeSpan maximumWindow,
    string windowDirectory) : ITranscriptionEngine
{
    public async Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        WhisperNativeRuntimeBootstrap.Configure(options.NativeRuntimePath);
        var stopwatch = Stopwatch.StartNew();
        var regions = await detector.DetectAsync(audio, vadOptions, progress, cancellationToken);
        var windows = SpeechWindowPlanner.Plan(regions, maximumGap, maximumWindow);
        progress?.Report(new PipelineProgress("vad", 100, $"Planned {windows.Count} speech windows"));

        using var factory = WhisperFactory.FromPath(options.ModelPath);
        var merged = new List<TranscriptSegment>();
        Directory.CreateDirectory(windowDirectory);

        foreach (var window in windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress(
                "transcribe",
                window.Index * 100d / window.Count,
                $"Transcribing speech window {window.Index + 1}/{window.Count}"));

            var path = Path.Combine(windowDirectory, $"speech-{window.Index:0000}.wav");
            var extracted = await regionExtractor.ExtractAsync(
                audio, window.Start, window.Duration, path, cancellationToken);
            using var processor = BuildProcessor(factory, options);
            await using var stream = File.OpenRead(extracted.Path);

            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            {
                var text = segment.Text.Trim();
                if (text.Length == 0 || segment.End <= segment.Start)
                    continue;

                var absoluteStart = window.Start + segment.Start;
                var absoluteEnd = window.Start + segment.End;
                var midpoint = absoluteStart + TimeSpan.FromTicks((absoluteEnd - absoluteStart).Ticks / 2);
                if (!window.Regions.Any(region => midpoint >= region.Start && midpoint < region.End))
                    continue;

                var item = new TranscriptSegment(merged.Count, absoluteStart, absoluteEnd, text);
                merged.Add(item);
                double? percent = audio.Duration is { TotalSeconds: > 0 }
                    ? Math.Min(100, absoluteEnd.TotalSeconds / audio.Duration.Value.TotalSeconds * 100)
                    : null;
                progress?.Report(new PipelineProgress("transcribe", percent, text));
            }
        }

        stopwatch.Stop();
        var ordered = merged.OrderBy(segment => segment.Start).ThenBy(segment => segment.End)
            .Select((segment, index) => segment with { Index = index }).ToArray();
        return new TranscriptionResult(
            "Whisper.net/whisper.cpp VAD windows",
            Path.GetFileName(options.ModelPath),
            options.Language,
            stopwatch.Elapsed,
            ordered);
    }

    private static WhisperProcessor BuildProcessor(WhisperFactory factory, TranscriptionOptions options)
    {
        var builder = factory.CreateBuilder();
        if (string.Equals(options.Language, "auto", StringComparison.OrdinalIgnoreCase))
            builder.WithLanguageDetection();
        else
            builder.WithLanguage(options.Language);
        if (options.TranslateToEnglish)
            builder.WithTranslate();
        if (options.Threads is > 0)
            builder.WithThreads(options.Threads.Value);
        if (options.NoContext)
            builder.WithNoContext();
        return builder.Build();
    }
}
