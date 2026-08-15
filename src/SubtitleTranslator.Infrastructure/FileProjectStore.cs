using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Infrastructure;

public sealed class FileProjectStore(string projectDirectory) : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private string ManifestPath => Path.Combine(Path.GetFullPath(projectDirectory), "project.json");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var result = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        result.Converters.Add(new JsonStringEnumConverter());
        return result;
    }

    public async Task<SubtitleProjectManifest?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ManifestPath))
            return null;
        await using var stream = File.OpenRead(ManifestPath);
        return await JsonSerializer.DeserializeAsync<SubtitleProjectManifest>(
            stream, JsonOptions, cancellationToken);
    }

    public async Task SaveAsync(
        SubtitleProjectManifest project, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var temporary = $"{ManifestPath}.{Guid.NewGuid():N}.tmp";
        await using (var stream = new FileStream(
            temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true))
            await JsonSerializer.SerializeAsync(stream, project, JsonOptions, cancellationToken);
        File.Move(temporary, ManifestPath, overwrite: true);
    }
}
