using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SubtitleTranslator.Infrastructure;

public static class PipelineCacheKeyBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Build(
        string stage,
        int schemaVersion,
        object configuration,
        params string[] upstreamKeys)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            stage,
            schemaVersion,
            configuration,
            upstreamKeys
        }, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
