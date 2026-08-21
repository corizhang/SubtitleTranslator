using SubtitleTranslator.Application;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.IntegrationTests;

public sealed class BatchQueueStoreTests
{
    [Fact]
    public async Task Queue_round_trip_restores_interrupted_item_as_pending()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"batch-queue-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "queue.json");
        try
        {
            var store = new JsonBatchQueueStore(path);
            var running = new BatchQueueEntry(Guid.NewGuid(), @"D:\video.mkv", BatchTaskState.Running,
                42, "翻译", null, null, DateTime.UtcNow);
            await store.SaveAsync(new BatchQueueSnapshot(1, [running]), CancellationToken.None);

            var restored = Assert.Single((await store.LoadAsync(CancellationToken.None)).Items);

            Assert.Equal(BatchTaskState.Pending, restored.State);
            Assert.Contains("中断", restored.Stage);
            Assert.Equal(42, restored.Progress);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Corrupt_queue_is_quarantined_and_does_not_block_startup()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"batch-queue-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "queue.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, "{not-json");

            var snapshot = await new JsonBatchQueueStore(path).LoadAsync(CancellationToken.None);

            Assert.Empty(snapshot.Items);
            Assert.Single(Directory.GetFiles(directory, "queue.json.invalid-*"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Batch_history_round_trip_preserves_execution_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"batch-history-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "history.json");
        try
        {
            var item = new BatchArchiveItem(@"D:\video.mkv", BatchTaskState.Completed, 100,
                "字幕已生成", null, @"D:\video.zh.srt", @"D:\projects\video");
            var batch = new BatchArchive(Guid.NewGuid(), "Season 1", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow, [item]);
            var store = new JsonBatchHistoryStore(path);

            await store.SaveAsync(new BatchArchiveSnapshot(1, [batch]), CancellationToken.None);
            var restored = Assert.Single((await store.LoadAsync(CancellationToken.None)).Batches);

            Assert.Equal("Season 1", restored.Name);
            Assert.Equal(BatchTaskState.Completed, Assert.Single(restored.Items).State);
            Assert.Equal(@"D:\projects\video", restored.Items[0].ProjectDirectory);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
