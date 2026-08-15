using SubtitleTranslator.Application;

namespace SubtitleTranslator.App;

public static class ComponentCatalog
{
    public static DownloadableComponent Vad { get; } = new(
        "silero-vad", "Silero VAD 6.2", "6.2.0", "ggml-silero-v6.2.0.bin", 885_098,
        "2AA269B785EEB53A82983A20501DDF7C1D9C48E33AB63A41391AC6C9F7FB6987",
        new Uri("https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-silero-v6.2.0.bin"),
        ComponentArchiveType.RawFile, "vad", null, "ggml-silero-v6.2.0.bin");

    public static DownloadableComponent CpuRuntime { get; } = new(
        "whisper-runtime-cpu", "Whisper CPU runtime", "1.9.1", "whisper.net.runtime.1.9.1.nupkg", 18_482_556,
        "B5224F0DAD44D5EB8233E5D83F4333A8A3FCCADC77095F50F361F06D65E0736B",
        new Uri("https://api.nuget.org/v3-flatcontainer/whisper.net.runtime/1.9.1/whisper.net.runtime.1.9.1.nupkg"),
        ComponentArchiveType.Zip, "runtime-cpu-1.9.1", "build/win-x64/", "whisper.dll");

    public static DownloadableComponent CudaRuntime { get; } = new(
        "whisper-runtime-cuda", "Whisper CUDA runtime", "1.9.1", "whisper.net.runtime.cuda.windows.1.9.1.nupkg", 142_586_522,
        "3B765C9690C114825AF1E9FD48E8F2E6F8B798C314D29E27E09640DA479DFCEB",
        new Uri("https://api.nuget.org/v3-flatcontainer/whisper.net.runtime.cuda.windows/1.9.1/whisper.net.runtime.cuda.windows.1.9.1.nupkg"),
        ComponentArchiveType.Zip, "runtime-cuda-1.9.1", "build/win-x64/", "whisper.dll");
}
