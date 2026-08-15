using System.ComponentModel;
using System.Runtime.InteropServices;
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

        var root = Path.GetDirectoryName(fullPath)!;
        var isCuda = File.Exists(Path.Combine(root, "ggml-cuda-whisper.dll"));
        var loaderDirectory = Path.Combine(root, "runtimes", isCuda ? "cuda" : string.Empty, "win-x64");
        EnsureLoaderLayout(root, loaderDirectory);

        // Whisper.net treats LibraryPath as an anchor and probes runtimes/* beneath its directory.
        RuntimeOptions.LibraryPath = Path.Combine(root, "runtime.anchor");
        RuntimeOptions.RuntimeLibraryOrder = [isCuda ? RuntimeLibrary.Cuda : RuntimeLibrary.Cpu];
        configuredPath = fullPath;
    }

    private static void EnsureLoaderLayout(string sourceDirectory, string loaderDirectory)
    {
        Directory.CreateDirectory(loaderDirectory);
        foreach (var source in Directory.EnumerateFiles(sourceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var target = Path.Combine(loaderDirectory, Path.GetFileName(source));
            if (File.Exists(target)) continue;
            if (!OperatingSystem.IsWindows() || !CreateHardLink(target, source, IntPtr.Zero))
            {
                try { File.Copy(source, target, false); }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(
                        $"无法为 Whisper runtime 创建兼容加载目录：{loaderDirectory}",
                        new Win32Exception(Marshal.GetLastWin32Error(), exception.Message));
                }
            }
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);
}
