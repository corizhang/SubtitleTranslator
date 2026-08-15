using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Domain.Tests;

public sealed class LocalApiKeyResolverTests
{
    [Fact]
    public void ReadDeepSeekApiKey_ReadsQuotedValueFromDotEnv()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"dotenv-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, ".env"), "# test\nDEEPSEEK_API_KEY=\"local-secret\"\n");

            Assert.Equal("local-secret", LocalApiKeyResolver.ReadDeepSeekApiKey(directory, includeEnvironment: false));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
