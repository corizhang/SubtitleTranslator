namespace SubtitleTranslator.Translation;

public sealed record DeepSeekTranslationOptions(
    string ApiKey,
    string Model = "deepseek-v4-flash",
    string BaseUrl = "https://api.deepseek.com",
    int MaximumAttempts = 4,
    TimeSpan? InitialRetryDelay = null,
    int MaximumOutputTokens = 8192)
{
    public TimeSpan RetryDelay => InitialRetryDelay ?? TimeSpan.FromSeconds(2);
}
