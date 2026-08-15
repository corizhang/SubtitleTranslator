using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class JsonBatchQueueStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<BatchQueueSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new BatchQueueSnapshot(1, []);
        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<BatchQueueSnapshot>(stream, Options, cancellationToken)
                ?? new BatchQueueSnapshot(1, []);
            return snapshot with
            {
                Items = snapshot.Items.Select(item => item.State == BatchTaskState.Running
                    ? item with { State = BatchTaskState.Pending, Stage = "上次运行中断，等待恢复", Error = null }
                    : item).ToArray()
            };
        }
        catch (JsonException)
        {
            File.Move(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}", true);
            return new BatchQueueSnapshot(1, []);
        }
    }

    public async Task SaveAsync(BatchQueueSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, Options), cancellationToken);
        File.Move(temporary, path, true);
    }
}
