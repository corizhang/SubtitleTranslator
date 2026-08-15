using System.IO.Compression;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class ComponentInstallService(HttpClient httpClient) : IComponentInstallService
{
    public async Task<ComponentInstallResult> InstallAsync(
        DownloadableComponent component,
        string componentsRoot,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var downloads = Path.Combine(componentsRoot, "downloads");
        var descriptor = new DownloadableModel(
            component.Id, component.DisplayName, component.DownloadFileName,
            component.DownloadSizeBytes, component.Sha256, component.DownloadUri, component.Version);
        var archive = await new HttpModelDownloadService(httpClient)
            .DownloadAsync(descriptor, downloads, progress, cancellationToken);
        var installDirectory = Path.Combine(componentsRoot, component.InstallDirectoryName);
        var staging = installDirectory + $".installing-{Guid.NewGuid():N}";
        Directory.CreateDirectory(staging);
        try
        {
            if (component.ArchiveType == ComponentArchiveType.RawFile)
                File.Copy(archive, Path.Combine(staging, Path.GetFileName(component.RequiredRelativePath)), true);
            else
                await ExtractSelectedZipEntriesAsync(archive, staging, component.ZipEntryPrefix, cancellationToken);

            var requiredInStaging = Path.Combine(staging, component.RequiredRelativePath);
            if (!File.Exists(requiredInStaging))
                throw new InvalidDataException($"组件包中缺少 {component.RequiredRelativePath}。");
            if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);
            Directory.Move(staging, installDirectory);
            return new ComponentInstallResult(
                component.Id, installDirectory, Path.Combine(installDirectory, component.RequiredRelativePath));
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    private static async Task ExtractSelectedZipEntriesAsync(
        string archivePath, string destination, string? prefix, CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var normalizedPrefix = prefix?.Replace('\\', '/').TrimStart('/');
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = entry.FullName.Replace('\\', '/');
            if (normalizedPrefix is not null && !name.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var relative = normalizedPrefix is null ? name : name[normalizedPrefix.Length..].TrimStart('/');
            if (relative.Length == 0 || entry.Name.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(destination, relative));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("组件压缩包包含不安全路径。");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
