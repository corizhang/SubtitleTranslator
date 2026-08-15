namespace SubtitleTranslator.Speech;

public static class CudaRuntimeBootstrap
{
    public static IReadOnlyList<string> AddToolkitDirectoriesToPath()
    {
        if (!OperatingSystem.IsWindows())
            return [];

        var candidates = new List<string>();
        AddFromEnvironment(candidates, "CUDA_PATH");

        var toolkitRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA GPU Computing Toolkit", "CUDA");
        if (Directory.Exists(toolkitRoot))
        {
            foreach (var directory in Directory.GetDirectories(toolkitRoot, "v*"))
                candidates.Add(directory);
        }

        var additions = candidates
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => new[] { Path.Combine(path, "bin", "x64"), Path.Combine(path, "bin") })
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (additions.Length == 0)
            return [];

        var currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var existing = currentPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var missing = additions
            .Where(path => !existing.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (missing.Length > 0)
            Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, missing) + Path.PathSeparator + currentPath);

        return missing;
    }

    private static void AddFromEnvironment(ICollection<string> candidates, string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            candidates.Add(path);
    }
}

