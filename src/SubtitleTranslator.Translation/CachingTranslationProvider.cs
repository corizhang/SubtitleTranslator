using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public interface ICompleteTranslationBatchCache
{
    Task CacheCompleteAsync(TranslationBatch batch, TranslationContext context,
        IReadOnlyList<TranslationSegment> translations, CancellationToken cancellationToken);
}

public sealed class CachingTranslationProvider(
    ITranslationProvider inner,
    string cacheDirectory,
    string providerVersion) : ITranslationProvider, ICompleteTranslationBatchCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        var path = GetCachePath(batch, context);

        if (File.Exists(path))
        {
            try
            {
                var cached = JsonSerializer.Deserialize<TranslationSegment[]>(
                    await File.ReadAllTextAsync(path, cancellationToken), JsonOptions);
                if (cached is not null)
                    return TranslationResponseValidator.ValidateAndOrder(batch.Segments, cached);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                try { File.Move(path, path + $".invalid-{DateTime.UtcNow:yyyyMMddHHmmss}", true); }
                catch (IOException) { }
            }
        }

        var translated = await inner.TranslateAsync(batch, context, cancellationToken);
        IReadOnlyList<TranslationSegment> validated;
        try { validated = TranslationResponseValidator.ValidateAndOrder(batch.Segments, translated); }
        catch (InvalidOperationException) { return translated; }
        await CacheCompleteAsync(batch, context, validated, cancellationToken);
        return validated;
    }

    public async Task CacheCompleteAsync(TranslationBatch batch, TranslationContext context,
        IReadOnlyList<TranslationSegment> translations, CancellationToken cancellationToken)
    {
        var validated = TranslationResponseValidator.ValidateAndOrder(batch.Segments, translations);
        var path = GetCachePath(batch, context);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileNameWithoutExtension(path)}.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(validated, JsonOptions), cancellationToken);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private string GetCachePath(TranslationBatch batch, TranslationContext context)
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
        return Path.Combine(directory, $"{key}.json");
    }
}
