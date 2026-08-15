namespace SubtitleTranslator.Domain;

public enum PipelineStageState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed record SourceFileFingerprint(
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    string Sha256);

public sealed record PipelineStageRecord(
    string Stage,
    string CacheKey,
    PipelineStageState State,
    DateTime UpdatedUtc,
    IReadOnlyList<string> Artifacts,
    string? Error = null);

public sealed record SubtitleProjectManifest(
    int SchemaVersion,
    Guid ProjectId,
    string Name,
    SourceFileFingerprint Source,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyDictionary<string, PipelineStageRecord> Stages);
