using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Infrastructure;

public sealed class ProjectRunTracker
{
    private readonly FileProjectStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SubtitleProjectManifest _manifest;

    private ProjectRunTracker(FileProjectStore store, SubtitleProjectManifest manifest)
    {
        _store = store;
        _manifest = manifest;
    }

    public SubtitleProjectManifest Snapshot => _manifest;

    public static async Task<ProjectRunTracker> OpenAsync(
        string projectDirectory,
        string projectName,
        SourceFileFingerprint source,
        CancellationToken cancellationToken)
    {
        var store = new FileProjectStore(projectDirectory);
        var existing = await store.LoadAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var manifest = existing is not null && existing.Source.Sha256 == source.Sha256
            ? existing with { Source = source, UpdatedUtc = now }
            : new SubtitleProjectManifest(
                1, Guid.NewGuid(), projectName, source, now, now,
                new Dictionary<string, PipelineStageRecord>(StringComparer.OrdinalIgnoreCase));
        var tracker = new ProjectRunTracker(store, manifest);
        await store.SaveAsync(manifest, cancellationToken);
        return tracker;
    }

    public Task BeginAsync(string stage, string cacheKey, CancellationToken cancellationToken) =>
        UpdateAsync(stage, cacheKey, PipelineStageState.Running, [], null, cancellationToken);

    public Task CompleteAsync(
        string stage, string cacheKey, IReadOnlyList<string> artifacts,
        CancellationToken cancellationToken) =>
        UpdateAsync(stage, cacheKey, PipelineStageState.Completed, artifacts, null, cancellationToken);

    public Task FailAsync(
        string stage, string cacheKey, Exception exception,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(stage, cacheKey, PipelineStageState.Failed, [], Sanitize(exception.Message), cancellationToken);

    public Task CancelAsync(
        string stage, string cacheKey,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(stage, cacheKey, PipelineStageState.Cancelled, [], "Cancelled by user.", cancellationToken);

    private async Task UpdateAsync(
        string stage,
        string cacheKey,
        PipelineStageState state,
        IReadOnlyList<string> artifacts,
        string? error,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = DateTime.UtcNow;
            var stages = new Dictionary<string, PipelineStageRecord>(
                _manifest.Stages, StringComparer.OrdinalIgnoreCase)
            {
                [stage] = new PipelineStageRecord(
                    stage, cacheKey, state, now, artifacts, error)
            };
            _manifest = _manifest with { UpdatedUtc = now, Stages = stages };
            await _store.SaveAsync(_manifest, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Sanitize(string message)
    {
        var singleLine = message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= 1000 ? singleLine : singleLine[..1000];
    }
}
