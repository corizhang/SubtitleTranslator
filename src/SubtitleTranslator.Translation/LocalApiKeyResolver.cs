namespace SubtitleTranslator.Translation;

public static class LocalApiKeyResolver
{
    public static string? ReadDeepSeekApiKey(
        string? workingDirectory = null,
        bool includeEnvironment = true)
    {
        if (includeEnvironment)
        {
            var processValue = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            if (!string.IsNullOrWhiteSpace(processValue))
                return processValue.Trim();

            var userValue = Environment.GetEnvironmentVariable(
                "DEEPSEEK_API_KEY", EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue))
                return userValue.Trim();
        }

        var directory = Path.GetFullPath(workingDirectory ?? Environment.CurrentDirectory);
        var path = Path.Combine(directory, ".env");
        if (!File.Exists(path))
            return null;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim().TrimStart('\uFEFF');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var separator = line.IndexOf('=');
            if (separator <= 0)
                continue;
            var name = line[..separator].Trim();
            if (!string.Equals(name, "DEEPSEEK_API_KEY", StringComparison.Ordinal))
                continue;
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\'' && value[^1] == '\'')))
                value = value[1..^1];
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }
}
