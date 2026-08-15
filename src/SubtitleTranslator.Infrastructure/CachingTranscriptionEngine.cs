using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Infrastructure;

public sealed class CachingTranscriptionEngine(
    ITranscriptionEngine inner,
    IStageCache cache,
    string cacheKey) : ITranscriptionEngine
{
    public async Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await cache.ReadAsync<TranscriptionResult>(
            "transcription", cacheKey, cancellationToken);
        if (cached is not null)
        {
            progress?.Report(new PipelineProgress(
                "transcribe", 100, $"Transcription cache hit ({cached.Segments.Count} segments)"));
            return cached with { Engine = $"{cached.Engine} [cache]" };
        }

        var result = await inner.TranscribeAsync(audio, options, progress, cancellationToken);
        await cache.WriteAsync("transcription", cacheKey, result, cancellationToken);
        return result;
    }
}
