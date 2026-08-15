using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class JsonUserSettingsStore(string settingsPath) : IUserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(settingsPath)) return new UserSettings();
        try
        {
            await using var stream = File.OpenRead(settingsPath);
            return await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions, cancellationToken)
                ?? new UserSettings();
        }
        catch (JsonException)
        {
            return new UserSettings();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new InvalidOperationException("设置文件目录无效。");
        Directory.CreateDirectory(directory);
        var temporary = settingsPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(settings, JsonOptions), cancellationToken);
        File.Move(temporary, settingsPath, true);
    }
}
