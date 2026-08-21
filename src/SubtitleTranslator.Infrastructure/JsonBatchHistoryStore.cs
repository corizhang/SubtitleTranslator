using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class JsonBatchHistoryStore(string path)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<BatchArchiveSnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new BatchArchiveSnapshot(1, []);
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BatchArchiveSnapshot>(stream, Options, cancellationToken)
                ?? new BatchArchiveSnapshot(1, []);
        }
        catch (JsonException)
        {
            File.Move(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}", true);
            return new BatchArchiveSnapshot(1, []);
        }
    }

    public async Task SaveAsync(BatchArchiveSnapshot snapshot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(snapshot, Options), cancellationToken);
        File.Move(temporary, path, true);
    }
}
