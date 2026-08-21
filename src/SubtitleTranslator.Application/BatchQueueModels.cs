namespace SubtitleTranslator.Application;

public enum BatchTaskState { Pending, Running, Completed, Failed, Cancelled }

public sealed record BatchQueueEntry(
    Guid Id,
    string MediaPath,
    BatchTaskState State,
    double Progress,
    string Stage,
    string? Error,
    string? SubtitlePath,
    DateTime UpdatedUtc);

public sealed record BatchQueueSnapshot(
    int SchemaVersion,
    IReadOnlyList<BatchQueueEntry> Items,
    Guid? BatchId = null,
    string? Name = null,
    DateTime? CreatedUtc = null);

public sealed record BatchArchiveItem(
    string MediaPath,
    BatchTaskState State,
    double Progress,
    string Stage,
    string? Error,
    string? SubtitlePath,
    string? ProjectDirectory)
{
    public string Name => Path.GetFileNameWithoutExtension(MediaPath);
    public string StateDisplay => State switch
    {
        BatchTaskState.Completed => "已完成",
        BatchTaskState.Failed => "失败",
        BatchTaskState.Cancelled => "已取消",
        BatchTaskState.Running => "执行中断",
        _ => "未执行"
    };
}

public sealed record BatchArchive(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime ArchivedUtc,
    IReadOnlyList<BatchArchiveItem> Items);

public sealed record BatchArchiveSnapshot(int SchemaVersion, IReadOnlyList<BatchArchive> Batches);

public sealed record BatchExecutionResult(string SubtitlePath, string Message);
