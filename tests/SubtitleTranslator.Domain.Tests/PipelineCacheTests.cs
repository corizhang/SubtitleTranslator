using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.Domain.Tests;

public sealed class PipelineCacheTests
{
    [Fact]
    public void Keys_InvalidateOnlyChangedStageAndDownstream()
    {
        var source = "source-hash";
        var audio = PipelineCacheKeyBuilder.Build("audio", 1, new { stream = 1 }, source);
        var transcriptA = PipelineCacheKeyBuilder.Build("transcription", 1, new { model = "turbo" }, audio);
        var transcriptB = PipelineCacheKeyBuilder.Build("transcription", 1, new { model = "small" }, audio);
        var translationA = PipelineCacheKeyBuilder.Build("translation", 1, new { model = "flash" }, transcriptA);
        var translationB = PipelineCacheKeyBuilder.Build("translation", 1, new { model = "pro" }, transcriptA);
        var exportChinese = PipelineCacheKeyBuilder.Build("export", 1, new { layout = "chinese" }, translationA);
        var exportBilingual = PipelineCacheKeyBuilder.Build("export", 1, new { layout = "bilingual" }, translationA);

        Assert.NotEqual(transcriptA, transcriptB);
        Assert.NotEqual(translationA, translationB);
        Assert.NotEqual(exportChinese, exportBilingual);
        Assert.Equal(audio, PipelineCacheKeyBuilder.Build("audio", 1, new { stream = 1 }, source));
        Assert.Equal(transcriptA, PipelineCacheKeyBuilder.Build("transcription", 1, new { model = "turbo" }, audio));
    }

    [Fact]
    public async Task CachingTranscriptionEngine_SkipsInnerEngineOnSecondRun()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"stage-cache-{Guid.NewGuid():N}");
        try
        {
            var cache = new FileStageCache(directory);
            var inner = new CountingTranscriptionEngine();
            var engine = new CachingTranscriptionEngine(inner, cache, "key");
            var audio = new AudioArtifact("unused.wav", TimeSpan.FromSeconds(1), 1);
            var options = new TranscriptionOptions("model.bin", "en");

            var first = await engine.TranscribeAsync(audio, options, null, CancellationToken.None);
            var second = await engine.TranscribeAsync(audio, options, null, CancellationToken.None);

            Assert.Equal(1, inner.Calls);
            Assert.Single(first.Segments);
            Assert.Contains("[cache]", second.Engine);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class CountingTranscriptionEngine : ITranscriptionEngine
    {
        public int Calls { get; private set; }

        public Task<TranscriptionResult> TranscribeAsync(
            AudioArtifact audio, TranscriptionOptions options,
            IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TranscriptionResult(
                "test", "model", "en", TimeSpan.Zero,
                [new TranscriptSegment(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "hello")]));
        }
    }
}
