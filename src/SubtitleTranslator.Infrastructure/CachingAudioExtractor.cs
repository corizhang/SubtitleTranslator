using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Infrastructure;

public sealed class CachingAudioExtractor(
    IAudioExtractor inner,
    IStageCache cache,
    string cacheKey) : IAudioExtractor
{
    public async Task<AudioArtifact> ExtractAsync(
        string mediaPath,
        int streamIndex,
        string outputPath,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var cached = await cache.ReadAsync<AudioArtifact>("audio", cacheKey, cancellationToken);
        if (cached is not null && File.Exists(cached.Path))
        {
            progress?.Report(new PipelineProgress("extract", 100, "Audio cache hit"));
            return cached;
        }

        var result = await inner.ExtractAsync(
            mediaPath, streamIndex, outputPath, progress, cancellationToken);
        await cache.WriteAsync("audio", cacheKey, result, cancellationToken);
        return result;
    }
}
