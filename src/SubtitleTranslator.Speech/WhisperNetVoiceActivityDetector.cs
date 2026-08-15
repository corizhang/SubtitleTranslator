using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using Whisper.net;

namespace SubtitleTranslator.Speech;

public sealed class WhisperNetVoiceActivityDetector : IVoiceActivityDetector
{
    public async Task<IReadOnlyList<SpeechRegion>> DetectAsync(
        AudioArtifact audio,
        VoiceActivityOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(audio.Path))
            throw new FileNotFoundException("Audio file was not found.", audio.Path);
        if (!File.Exists(options.ModelPath))
            throw new FileNotFoundException("Silero VAD model was not found.", options.ModelPath);

        CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        progress?.Report(new PipelineProgress("vad", 0, "Loading Silero VAD model"));

        using var factory = WhisperVadFactory.FromPath(options.ModelPath);
        var builder = factory.CreateBuilder()
            .WithThreshold(options.Threshold)
            .WithUseGpu(options.UseGpu);

        if (options.MinimumSpeechDuration is { } minimumSpeech)
            builder.WithMinSpeechDuration(minimumSpeech);
        if (options.MinimumSilenceDuration is { } minimumSilence)
            builder.WithMinSilenceDuration(minimumSilence);
        if (options.SpeechPadding is { } padding)
            builder.WithSpeechPadding(padding);

        using var processor = builder.Build();
        await using var stream = File.OpenRead(audio.Path);
        var detected = await processor.DetectSpeechAsync(stream, cancellationToken);
        var regions = detected
            .Where(segment => segment.End > segment.Start)
            .Select(segment => new SpeechRegion(segment.Start, segment.End))
            .OrderBy(segment => segment.Start)
            .ToArray();

        progress?.Report(new PipelineProgress(
            "vad",
            100,
            $"Detected {regions.Length} speech regions ({regions.Sum(region => region.Duration.TotalSeconds):0.0}s)"));
        return regions;
    }
}
