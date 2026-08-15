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

public sealed record BatchQueueSnapshot(int SchemaVersion, IReadOnlyList<BatchQueueEntry> Items);

public sealed record BatchExecutionResult(string SubtitlePath, string Message);
