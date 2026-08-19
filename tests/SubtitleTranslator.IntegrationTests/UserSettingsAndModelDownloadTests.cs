using System.Net;
using System.Security.Cryptography;
using System.IO.Compression;
using SubtitleTranslator.Application;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.IntegrationTests;

public sealed class UserSettingsAndModelDownloadTests
{
    [Fact]
    public async Task Hardware_diagnostics_is_safe_without_configured_runtime()
    {
        var report = await new WindowsHardwareDiagnosticService()
            .DiagnoseAsync(new UserSettings(), CancellationToken.None);
        Assert.False(report.HasWhisperRuntime);
        Assert.Equal("未配置", report.RuntimeKind);
        Assert.NotEmpty(report.Warnings);
    }

    [Fact]
    public async Task Component_installer_extracts_only_selected_runtime_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-install-{Guid.NewGuid():N}");
        var package = CreateZip(("build/win-x64/whisper.dll", "runtime"), ("build/linux-x64/libwhisper.so", "linux"));
        var sha = Convert.ToHexString(SHA256.HashData(package));
        try
        {
            using var client = new HttpClient(new StaticHandler(package));
            var component = new DownloadableComponent(
                "cpu", "CPU", "1", "runtime.nupkg", package.Length, sha,
                new Uri("https://example.invalid/runtime.nupkg"), ComponentArchiveType.Zip,
                "runtime-cpu", "build/win-x64/", "whisper.dll");
            var result = await new ComponentInstallService(client).InstallAsync(component, directory, null, CancellationToken.None);
            Assert.True(File.Exists(result.RequiredPath));
            Assert.False(File.Exists(Path.Combine(result.InstallDirectory, "libwhisper.so")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Component_installer_rejects_zip_path_traversal()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-unsafe-{Guid.NewGuid():N}");
        var package = CreateZip(("build/win-x64/../../escape.dll", "bad"));
        var sha = Convert.ToHexString(SHA256.HashData(package));
        try
        {
            using var client = new HttpClient(new StaticHandler(package));
            var component = new DownloadableComponent(
                "unsafe", "Unsafe", "1", "unsafe.zip", package.Length, sha,
                new Uri("https://example.invalid/unsafe.zip"), ComponentArchiveType.Zip,
                "runtime", "build/win-x64/", "whisper.dll");
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new ComponentInstallService(client).InstallAsync(component, directory, null, CancellationToken.None));
            Assert.False(File.Exists(Path.Combine(directory, "escape.dll")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Environment_diagnostics_require_all_runtime_components()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-components-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var file in new[] { "ffmpeg.exe", "ffprobe.exe", "model.bin", "ggml-silero-vad.bin", "whisper.dll" })
                await File.WriteAllTextAsync(Path.Combine(directory, file), "test");
            var settings = new UserSettings(
                WhisperModelPath: Path.Combine(directory, "model.bin"),
                VadModelPath: Path.Combine(directory, "ggml-silero-vad.bin"),
                FfmpegPath: Path.Combine(directory, "ffmpeg.exe"),
                FfprobePath: Path.Combine(directory, "ffprobe.exe"),
                WhisperRuntimePath: directory);
            var report = await new EnvironmentDiagnosticService().DiagnoseAsync(settings, CancellationToken.None);
            Assert.True(report.CanGenerateSubtitles);
            Assert.All(report.Components.Where(item => item.Id != "vlc-runtime"),
                item => Assert.Equal(ComponentState.Ready, item.State));
            Assert.Contains(report.Components, item => item.Id == "vlc-runtime");
            File.Delete(Path.Combine(directory, "whisper.dll"));
            report = await new EnvironmentDiagnosticService().DiagnoseAsync(settings, CancellationToken.None);
            Assert.False(report.CanGenerateSubtitles);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Environment_diagnostics_validate_optional_vlc_runtime_without_blocking_generation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-vlc-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "libvlc.dll"), "test");
            var report = await new EnvironmentDiagnosticService().DiagnoseAsync(
                new UserSettings(VlcRuntimePath: directory), CancellationToken.None);
            Assert.Equal(ComponentState.Invalid, report.Components.Single(x => x.Id == "vlc-runtime").State);

            if (!OperatingSystem.IsWindows() || !Environment.Is64BitOperatingSystem) return;
            var amd64Library = Path.Combine(Environment.SystemDirectory, "version.dll");
            File.Copy(amd64Library, Path.Combine(directory, "libvlc.dll"), true);
            File.Copy(amd64Library, Path.Combine(directory, "libvlccore.dll"), true);
            Directory.CreateDirectory(Path.Combine(directory, "plugins"));
            report = await new EnvironmentDiagnosticService().DiagnoseAsync(
                new UserSettings(VlcRuntimePath: directory), CancellationToken.None);
            Assert.Equal(ComponentState.Ready, report.Components.Single(x => x.Id == "vlc-runtime").State);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Environment_diagnostics_reject_whisper_model_as_vad()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-invalid-vad-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            var wrongModel = Path.Combine(directory, "ggml-medium-q5_0.bin");
            await File.WriteAllBytesAsync(wrongModel, new byte[1024]);
            var report = await new EnvironmentDiagnosticService().DiagnoseAsync(
                new UserSettings(VadModelPath: wrongModel), CancellationToken.None);
            Assert.Equal(ComponentState.Invalid, report.Components.Single(x => x.Id == "vad").State);
            Assert.False(report.CanGenerateSubtitles);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Dpapi_secret_round_trip_is_not_plaintext()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-secrets-{Guid.NewGuid():N}");
        const string secret = "sk-test-secret-value";
        try
        {
            var store = new WindowsDpapiSecretStore(directory);
            await store.WriteAsync("deepseek", secret, CancellationToken.None);
            Assert.Equal(secret, await store.ReadAsync("deepseek", CancellationToken.None));
            var bytes = await File.ReadAllBytesAsync(Path.Combine(directory, "deepseek.bin"));
            Assert.DoesNotContain(secret, Convert.ToBase64String(bytes));
            await store.DeleteAsync("deepseek", CancellationToken.None);
            Assert.Null(await store.ReadAsync("deepseek", CancellationToken.None));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Settings_round_trip_preserves_model_path()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-settings-{Guid.NewGuid():N}");
        try
        {
            var store = new JsonUserSettingsStore(Path.Combine(directory, "settings.json"));
            var expected = new UserSettings(
                WhisperModelPath: @"D:\models\custom.bin",
                SubtitlePublishLocation: SubtitlePublishLocation.CustomDirectory,
                SubtitleNamingStrategy: SubtitleNamingStrategy.CustomTemplate,
                SubtitleConflictPolicy: SubtitleConflictPolicy.AutoNumber,
                SubtitleCustomDirectory: @"D:\subtitles",
                SubtitleNamingTemplate: "{video-name}.{language}.edited.srt",
                DefaultOutputMode: "仅中文字幕",
                DefaultQualityMode: "生成建议清单",
                DefaultSourceLanguage: "日语",
                DefaultTranslationQaEnabled: false);
            await store.SaveAsync(expected, CancellationToken.None);
            var actual = await store.LoadAsync(CancellationToken.None);
            Assert.Equal(expected, actual);
            Assert.False(File.Exists(Path.Combine(directory, "settings.json.tmp")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Download_verifies_hash_before_promoting_partial_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"subtitle-model-{Guid.NewGuid():N}");
        var payload = "valid model payload"u8.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(payload));
        try
        {
            using var client = new HttpClient(new StaticHandler(payload));
            var service = new HttpModelDownloadService(client);
            var model = new DownloadableModel("test", "Test", "model.bin", payload.Length, sha,
                new Uri("https://example.invalid/model.bin"), "test");
            var path = await service.DownloadAsync(model, directory, null, CancellationToken.None);
            Assert.Equal(payload, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private sealed class StaticHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
    }

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        return stream.ToArray();
    }
}
