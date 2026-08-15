using Whisper.net.LibraryLoader;

namespace SubtitleTranslator.Speech;

public static class WhisperNativeRuntimeBootstrap
{
    private static string? configuredPath;

    public static void Configure(string? runtimeDirectory)
    {
        if (string.IsNullOrWhiteSpace(runtimeDirectory)) return;
        var library = Directory.Exists(runtimeDirectory)
            ? Path.Combine(runtimeDirectory, "whisper.dll") : runtimeDirectory;
        if (!File.Exists(library)) throw new FileNotFoundException("Whisper 原生运行库 whisper.dll 不存在。", library);
        var fullPath = Path.GetFullPath(library);
        if (configuredPath is not null && !configuredPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Whisper 原生运行库已加载，重启应用后才能切换运行组件。");
        RuntimeOptions.LibraryPath = fullPath;
        configuredPath = fullPath;
    }
}
