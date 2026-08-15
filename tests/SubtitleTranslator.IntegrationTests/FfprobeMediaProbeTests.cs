using SubtitleTranslator.Media;
using SubtitleTranslator.Speech;

namespace SubtitleTranslator.IntegrationTests;

public sealed class FfprobeMediaProbeTests
{
    [Fact]
    public async Task ProbeAsync_MissingFile_ThrowsFileNotFound()
    {
        var probe = new FfprobeMediaProbe();
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            probe.ProbeAsync(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None));
    }
}

public sealed class CudaRuntimeBootstrapTests
{
    [Fact]
    public void AddToolkitDirectoriesToPath_DoesNotAddDuplicates()
    {
        var first = CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();
        var second = CudaRuntimeBootstrap.AddToolkitDirectoriesToPath();

        Assert.Empty(second);
        Assert.DoesNotContain(first, path => !Directory.Exists(path));
    }
}
