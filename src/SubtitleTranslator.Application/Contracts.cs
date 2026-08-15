using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Application;

public interface IMediaProbe
{
    Task<MediaInfo> ProbeAsync(string mediaPath, CancellationToken cancellationToken);
}

public interface IAudioExtractor
{
    Task<AudioArtifact> ExtractAsync(
        string mediaPath,
        int streamIndex,
        string outputPath,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ITranscriptionEngine
{
    Task<TranscriptionResult> TranscribeAsync(
        AudioArtifact audio,
        TranscriptionOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IAudioChunker
{
    Task<IReadOnlyList<AudioChunk>> SplitAsync(
        AudioArtifact audio,
        TimeSpan chunkDuration,
        TimeSpan overlap,
        string outputDirectory,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IVoiceActivityDetector
{
    Task<IReadOnlyList<SpeechRegion>> DetectAsync(
        AudioArtifact audio,
        VoiceActivityOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IAudioRegionExtractor
{
    Task<AudioArtifact> ExtractAsync(
        AudioArtifact source,
        TimeSpan start,
        TimeSpan duration,
        string outputPath,
        CancellationToken cancellationToken);
}

public interface ISubtitleExporter
{
    Task ExportAsync(
        IReadOnlyList<TranscriptSegment> segments,
        string outputPath,
        CancellationToken cancellationToken);
}

public interface ITranslationProvider
{
    Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken);
}

public interface ITranslationReviewProvider
{
    Task<IReadOnlyList<TranslationReviewResult>> ReviewAsync(
        IReadOnlyList<TranslationReviewCandidate> candidates,
        TranslationContext context,
        CancellationToken cancellationToken);
}

public interface ISourceFileFingerprintService
{
    Task<SourceFileFingerprint> ComputeAsync(string path, CancellationToken cancellationToken);
}

public interface IStageCache
{
    Task<T?> ReadAsync<T>(string stage, string cacheKey, CancellationToken cancellationToken);
    Task WriteAsync<T>(string stage, string cacheKey, T value, CancellationToken cancellationToken);
}

public interface IProjectStore
{
    Task<SubtitleProjectManifest?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(SubtitleProjectManifest project, CancellationToken cancellationToken);
}

public interface ISubtitleGenerationService
{
    Task<SubtitleGenerationResult> GenerateAsync(
        SubtitleGenerationRequest request,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IUserSettingsStore
{
    Task<UserSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(UserSettings settings, CancellationToken cancellationToken);
}

public interface IModelDownloadService
{
    Task<string> DownloadAsync(
        DownloadableModel model,
        string destinationDirectory,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}

public interface ISecretStore
{
    Task<string?> ReadAsync(string name, CancellationToken cancellationToken);
    Task WriteAsync(string name, string value, CancellationToken cancellationToken);
    Task DeleteAsync(string name, CancellationToken cancellationToken);
}

public interface IEnvironmentDiagnosticService
{
    Task<EnvironmentDiagnosticReport> DiagnoseAsync(UserSettings settings, CancellationToken cancellationToken);
}

public interface IComponentInstallService
{
    Task<ComponentInstallResult> InstallAsync(
        DownloadableComponent component,
        string componentsRoot,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken);
}

public interface IHardwareDiagnosticService
{
    Task<HardwareDiagnosticReport> DiagnoseAsync(UserSettings settings, CancellationToken cancellationToken);
}

public interface IWhisperRuntimeSelfTestService
{
    Task<WhisperSelfTestResult> RunAsync(
        string modelPath,
        string runtimeDirectory,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken);
}
