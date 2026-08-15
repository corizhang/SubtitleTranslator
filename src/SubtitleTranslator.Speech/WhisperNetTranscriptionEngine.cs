using System.Diagnostics;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using Whisper.net;

namespace SubtitleTranslator.Speech;

public sealed class WhisperNetTranscriptionEngine : ITranscriptionEngine
{
    public async Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        WhisperNativeRuntimeBootstrap.Configure(options.NativeRuntimePath);

        if (!File.Exists(audio.Path))
            throw new FileNotFoundException("Audio file was not found.", audio.Path);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("Whisper model was not found.", options.ModelPath);

        progress?.Report(new PipelineProgress("transcribe", 0, "Loading Whisper model"));
        var stopwatch = Stopwatch.StartNew();
        using var factory = WhisperFactory.FromPath(options.ModelPath);
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

        using var processor = builder.Build();
        await using var stream = File.OpenRead(audio.Path);
        var segments = new List<TranscriptSegment>();

        await foreach (var segment in processor.ProcessAsync(stream, cancellationToken))
        {
            var text = segment.Text.Trim();
            if (text.Length == 0 || segment.End <= segment.Start)
                continue;

            var item = new TranscriptSegment(
                segments.Count,
                segment.Start,
                segment.End,
                text);
            segments.Add(item);

            double? percent = audio.Duration is { TotalSeconds: > 0 }
                ? Math.Min(100, item.End.TotalSeconds / audio.Duration.Value.TotalSeconds * 100)
                : null;
            progress?.Report(new PipelineProgress("transcribe", percent, item.Text));
        }

        stopwatch.Stop();
        return new TranscriptionResult(
            "Whisper.net/whisper.cpp",
            Path.GetFileName(options.ModelPath),
            options.Language,
            stopwatch.Elapsed,
            segments);
    }
}
