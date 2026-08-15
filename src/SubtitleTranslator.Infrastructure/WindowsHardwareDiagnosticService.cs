using System.Diagnostics;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class WindowsHardwareDiagnosticService : IHardwareDiagnosticService
{
    public async Task<HardwareDiagnosticReport> DiagnoseAsync(UserSettings settings, CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var gpu = await QueryNvidiaAsync(cancellationToken);
        var cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");
        var cudaVersion = cudaPath is null ? null : Path.GetFileName(cudaPath.TrimEnd(Path.DirectorySeparatorChar));
        var runtime = settings.WhisperRuntimePath;
        var hasRuntime = runtime is not null && File.Exists(Path.Combine(runtime, "whisper.dll"));
        var runtimeKind = runtime?.Contains("cuda", StringComparison.OrdinalIgnoreCase) == true ? "CUDA" : hasRuntime ? "CPU" : "未配置";

        if (runtimeKind == "CUDA" && gpu is null) warnings.Add("已选择 CUDA runtime，但未检测到 NVIDIA GPU 或 nvidia-smi。");
        if (runtimeKind == "CUDA" && cudaPath is null) warnings.Add("未检测到 CUDA Toolkit；当前 Whisper.net CUDA 组件可能无法加载所需 DLL。");
        if (!hasRuntime) warnings.Add("尚未配置包含 whisper.dll 的运行组件。 ");

        return new HardwareDiagnosticReport(
            gpu is not null, gpu?.Name, gpu?.Driver, gpu?.Compute,
            cudaPath is not null && Directory.Exists(cudaPath), cudaVersion,
            hasRuntime, runtimeKind, warnings);
    }

    private static async Task<GpuInfo?> QueryNvidiaAsync(CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name,driver_version,compute_cap --format=csv,noheader,nounits",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0) return null;
            var first = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var values = first?.Split(',', StringSplitOptions.TrimEntries);
            return values is { Length: >= 3 } ? new GpuInfo(values[0], values[1], values[2]) : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        { return null; }
    }

    private sealed record GpuInfo(string Name, string Driver, string Compute);
}
