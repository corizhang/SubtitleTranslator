using System.Text.Json;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class FileStageCache(string rootDirectory) : IStageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<T?> ReadAsync<T>(
        string stage, string cacheKey, CancellationToken cancellationToken)
    {
        var path = GetPath(stage, cacheKey);
        if (!File.Exists(path))
            return default;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public async Task WriteAsync<T>(
        string stage, string cacheKey, T value, CancellationToken cancellationToken)
    {
        var path = GetPath(stage, cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
            temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private string GetPath(string stage, string cacheKey)
    {
        var safeStage = string.Concat(stage.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        return Path.Combine(Path.GetFullPath(rootDirectory), safeStage, $"{cacheKey}.json");
    }
}
