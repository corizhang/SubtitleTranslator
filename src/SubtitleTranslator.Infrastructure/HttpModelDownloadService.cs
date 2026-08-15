using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class HttpModelDownloadService(HttpClient httpClient) : IModelDownloadService
{
    public async Task<string> DownloadAsync(
        DownloadableModel model,
        string destinationDirectory,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        var finalPath = Path.Combine(destinationDirectory, model.FileName);
        if (File.Exists(finalPath) && await HasExpectedHashAsync(finalPath, model.Sha256, cancellationToken))
            return finalPath;

        var partialPath = finalPath + ".partial";
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, model.DownloadUri);
        if (existingLength > 0) request.Headers.Range = new RangeHeaderValue(existingLength, null);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (existingLength > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentRange?.Length
            ?? (response.Content.Headers.ContentLength is long length ? existingLength + length : model.SizeBytes);
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(partialPath, FileMode.Append, FileAccess.Write, FileShare.Read, 1024 * 1024, true);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            var received = existingLength;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                var percent = total > 0 ? received * 100d / total : 0;
                progress?.Report(new ModelDownloadProgress(received, total, percent,
                    $"正在下载 {model.DisplayName}：{FormatBytes(received)} / {FormatBytes(total)}"));
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }

        await output.FlushAsync(cancellationToken);
        output.Close();
        progress?.Report(new ModelDownloadProgress(model.SizeBytes, model.SizeBytes, 100, "正在校验模型完整性……"));
        if (!await HasExpectedHashAsync(partialPath, model.Sha256, cancellationToken))
            throw new InvalidDataException("模型 SHA-256 校验失败，临时文件已保留，可重新下载。");
        File.Move(partialPath, finalPath, true);
        return finalPath;
    }

    private static async Task<bool> HasExpectedHashAsync(string path, string expected, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash).Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:0.00} GB"
        : $"{bytes / 1024d / 1024:0.0} MB";
}
