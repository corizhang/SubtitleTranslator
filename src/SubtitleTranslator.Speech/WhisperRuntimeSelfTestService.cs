using System.Diagnostics;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using Whisper.net;

namespace SubtitleTranslator.Speech;

public sealed class WhisperRuntimeSelfTestService : IWhisperRuntimeSelfTestService
{
    public async Task<WhisperSelfTestResult> RunAsync(
        string modelPath,
        string runtimeDirectory,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException("Whisper 模型不存在。", modelPath);
        WhisperNativeRuntimeBootstrap.Configure(runtimeDirectory);
        CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        var stopwatch = Stopwatch.StartNew();
        var wav = Path.Combine(Path.GetTempPath(), $"whisper-self-test-{Guid.NewGuid():N}.wav");
        try
        {
            WriteSilentWave(wav, 16000);
            progress?.Report(new PipelineProgress("self-test", 10, "正在加载 Whisper 模型与运行组件……"));
            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory.CreateBuilder().WithLanguageDetection().Build();
            await using var stream = File.OpenRead(wav);
            await foreach (var _ in processor.ProcessAsync(stream, cancellationToken)) { }
            stopwatch.Stop();
            var kind = runtimeDirectory.Contains("cuda", StringComparison.OrdinalIgnoreCase) ? "CUDA" : "CPU";
            return new WhisperSelfTestResult(true, stopwatch.Elapsed, kind, $"{kind} runtime 与模型推理成功。");
        }
        finally { if (File.Exists(wav)) File.Delete(wav); }
    }

    private static void WriteSilentWave(string path, int sampleRate)
    {
        var dataLength = sampleRate * 2;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8); writer.Write(36 + dataLength); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(dataLength); writer.Write(new byte[dataLength]);
    }
}
