using System.Diagnostics;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using Whisper.net;

namespace SubtitleTranslator.Speech;

public sealed class ChunkedWhisperNetTranscriptionEngine(
    IAudioChunker chunker,
    TimeSpan chunkDuration,
    TimeSpan overlap,
    string chunkDirectory) : ITranscriptionEngine
{
    public async Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        WhisperNativeRuntimeBootstrap.Configure(options.NativeRuntimePath);
        CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("Whisper model was not found.", options.ModelPath);

        var stopwatch = Stopwatch.StartNew();
        var chunks = await chunker.SplitAsync(
            audio, chunkDuration, overlap, chunkDirectory, progress, cancellationToken);
        progress?.Report(new PipelineProgress("transcribe", 0, "Loading Whisper model once for all chunks"));

        using var factory = WhisperFactory.FromPath(options.ModelPath);
        var merged = new List<TranscriptSegment>();

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PipelineProgress(
                "transcribe",
                chunk.Index * 100d / chunk.Count,
                $"Transcribing chunk {chunk.Index + 1}/{chunk.Count}"));

            using var processor = BuildProcessor(factory, options);
            await using var stream = File.OpenRead(chunk.Path);

            await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
            {
                var text = segment.Text.Trim();
                if (text.Length == 0 || segment.End <= segment.Start)
                    continue;

                var absoluteStart = chunk.MediaStart + segment.Start;
                var absoluteEnd = chunk.MediaStart + segment.End;
                var midpoint = absoluteStart + TimeSpan.FromTicks((absoluteEnd - absoluteStart).Ticks / 2);

                // Each overlap belongs to the chunk whose non-overlapped core contains the segment midpoint.
                if (midpoint < chunk.CoreStart || midpoint >= chunk.CoreEnd)
                    continue;

                var item = new TranscriptSegment(
                    merged.Count,
                    absoluteStart,
                    absoluteEnd,
                    text);
                merged.Add(item);

                double? percent = audio.Duration is { TotalSeconds: > 0 }
                    ? Math.Min(100, absoluteEnd.TotalSeconds / audio.Duration.Value.TotalSeconds * 100)
                    : null;
                progress?.Report(new PipelineProgress("transcribe", percent, item.Text));
            }
        }

        stopwatch.Stop();
        var ordered = merged
            .OrderBy(segment => segment.Start)
            .ThenBy(segment => segment.End)
            .Select((segment, index) => segment with { Index = index })
            .ToArray();

        return new TranscriptionResult(
            "Whisper.net/whisper.cpp chunked",
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
