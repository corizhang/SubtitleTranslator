using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class CachingTranslationProvider(
    ITranslationProvider inner,
    string cacheDirectory,
    string providerVersion) : ITranslationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var keyMaterial = JsonSerializer.Serialize(new
        {
            schema = 1,
            providerVersion,
            batch.SourceLanguage,
            batch.TargetLanguage,
            batch.Segments,
            context
        }, JsonOptions);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial))).ToLowerInvariant();
        var directory = Path.GetFullPath(cacheDirectory);
        var path = Path.Combine(directory, $"{key}.json");

        if (File.Exists(path))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<TranslationSegment[]>(
                    await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
                if (cached is not null)
                    return TranslationResponseValidator.ValidateAndOrder(batch.Segments, cached);
            }
            catch (JsonException)
            {
                // A corrupt or partial cache entry is treated as a miss and replaced atomically below.
            }
        }

        var translated = await inner.TranslateAsync(batch, context, cancellationToken);
        var validated = TranslationResponseValidator.ValidateAndOrder(batch.Segments, translated);
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{key}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(
            temporaryPath, JsonSerializer.Serialize(validated, JsonOptions), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
        return validated;
    }
}
