using System.Security.Cryptography;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Infrastructure;

public sealed class SourceFileFingerprintService : ISourceFileFingerprintService
{
    public async Task<SourceFileFingerprint> ComputeAsync(
        string path, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
            throw new FileNotFoundException("Source media file was not found.", fullPath);
        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new SourceFileFingerprint(
            fullPath, info.Length, info.LastWriteTimeUtc, Convert.ToHexString(hash).ToLowerInvariant());
    }
}
