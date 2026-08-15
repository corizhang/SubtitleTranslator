using System.IO;
using System.Text.RegularExpressions;

namespace SubtitleTranslator.App;

internal static partial class AppFileLogger
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private static readonly object Sync = new();
    public static string LogDirectory { get; } = Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "AI字幕翻译", "logs");
    public static string CurrentLogPath => Path.Combine(LogDirectory, "app.log");
    public static void Info(string message) => Write("INFO", message, null);
    public static void Error(string message, Exception? exception) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(CurrentLogPath) && new FileInfo(CurrentLogPath).Length >= MaximumLogBytes)
                    File.Move(CurrentLogPath, Path.Combine(LogDirectory, "app.previous.log"), true);
                var detail = exception is null ? string.Empty : Environment.NewLine + Redact(exception.ToString());
                File.AppendAllText(CurrentLogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] {Redact(message)}{detail}{Environment.NewLine}");
            }
        }
        catch { }
    }

    private static string Redact(string value) => ApiKeyPattern().Replace(value, "$1***");
    [GeneratedRegex("(?i)(api[-_ ]?key(?:\\s*[:=]\\s*)?|bearer\\s+)([^\\s,;]+)")]
    private static partial Regex ApiKeyPattern();
}
