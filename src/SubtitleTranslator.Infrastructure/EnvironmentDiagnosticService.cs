using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class EnvironmentDiagnosticService : IEnvironmentDiagnosticService
{
    public Task<EnvironmentDiagnosticReport> DiagnoseAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ffmpeg = ResolveExecutable(settings.FfmpegPath, "ffmpeg.exe");
        var ffprobe = ResolveExecutable(settings.FfprobePath, "ffprobe.exe");
        var items = new List<ComponentDiagnostic>
        {
            FileComponent("ffmpeg", "FFmpeg", ffmpeg, "请选择 FFmpeg，或在组件管理中下载。"),
            FileComponent("ffprobe", "FFprobe", ffprobe, "请选择 FFprobe，通常与 FFmpeg 位于同一目录。"),
            FileComponent("whisper-model", "Whisper 模型", settings.WhisperModelPath, "请选择本地模型或下载推荐模型。"),
            VadComponent(settings.VadModelPath),
            RuntimeComponent(settings.WhisperRuntimePath),
            VlcComponent(ResolveVlcDirectory(settings.VlcRuntimePath))
        };
        return Task.FromResult(new EnvironmentDiagnosticReport(items));
    }

    private static ComponentDiagnostic VlcComponent(string? directory)
    {
        if (directory is null)
            return new ComponentDiagnostic("vlc-runtime", "VLC 播放引擎", ComponentState.Optional,
                "未检测到 64 位 VLC；字幕校订仍可使用系统播放器或外部播放器。");

        var missing = new[] { "libvlc.dll", "libvlccore.dll", "plugins" }
            .Where(name => name == "plugins" ? !Directory.Exists(Path.Combine(directory, name)) : !File.Exists(Path.Combine(directory, name)))
            .ToArray();
        return missing.Length == 0
            ? new ComponentDiagnostic("vlc-runtime", "VLC 播放引擎", ComponentState.Ready,
                "已检测到完整的 LibVLC 运行时。", Path.GetFullPath(directory))
            : new ComponentDiagnostic("vlc-runtime", "VLC 播放引擎", ComponentState.Invalid,
                $"目录缺少 {string.Join("、", missing)}。", Path.GetFullPath(directory));
    }

    private static string? ResolveVlcDirectory(string? configured)
    {
        var candidates = new List<string?> { configured };
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles)) candidates.Add(Path.Combine(programFiles, "VideoLAN", "VLC"));
        return candidates.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.GetFullPath(x!))
            .FirstOrDefault(x => File.Exists(Path.Combine(x, "libvlc.dll")));
    }

    private static ComponentDiagnostic FileComponent(string id, string name, string? path, string missing) =>
        path is not null && File.Exists(path)
            ? new ComponentDiagnostic(id, name, ComponentState.Ready, "已就绪", Path.GetFullPath(path))
            : new ComponentDiagnostic(id, name, ComponentState.Missing, missing);

    private static ComponentDiagnostic RuntimeComponent(string? directory)
    {
        var library = directory is null ? null : Path.Combine(directory, "whisper.dll");
        return library is not null && File.Exists(library)
            ? new ComponentDiagnostic("whisper-runtime", "Whisper 运行组件", ComponentState.Ready, "已就绪", Path.GetFullPath(directory!))
            : new ComponentDiagnostic("whisper-runtime", "Whisper 运行组件", ComponentState.Missing,
                "请选择已解压的 CPU 或 CUDA runtime 目录。");
    }

    private static ComponentDiagnostic VadComponent(string? path)
    {
        if (path is null || !File.Exists(path))
            return new ComponentDiagnostic("vad", "Silero VAD", ComponentState.Missing, "请选择或下载 Silero VAD 模型。");
        var file = new FileInfo(path);
        if (!file.Name.Contains("silero", StringComparison.OrdinalIgnoreCase) || file.Length > 50L * 1024 * 1024)
            return new ComponentDiagnostic("vad", "Silero VAD", ComponentState.Invalid,
                "所选文件不像 Silero VAD（请勿选择 Whisper 语音模型）。", file.FullName);
        return new ComponentDiagnostic("vad", "Silero VAD", ComponentState.Ready, "已就绪", file.FullName);
    }

    private static string? ResolveExecutable(string? configured, string fileName)
    {
        if (configured is not null && File.Exists(configured)) return Path.GetFullPath(configured);
        foreach (var item in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(item, fileName);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
