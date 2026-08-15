using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;

namespace SubtitleTranslator.Infrastructure;

public sealed class SubtitlePublicationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string BuildTargetPath(SubtitlePublicationRequest request)
    {
        var media = Path.GetFullPath(request.MediaPath);
        var options = request.Options;
        if (options.Location == SubtitlePublishLocation.ProjectOnly)
            return Path.GetFullPath(request.SourceSubtitlePath);
        var directory = options.Location == SubtitlePublishLocation.VideoDirectory
            ? Path.GetDirectoryName(media)!
            : string.IsNullOrWhiteSpace(options.CustomDirectory)
                ? throw new InvalidOperationException("请选择自定义字幕输出目录。")
                : Path.GetFullPath(options.CustomDirectory);
        var videoName = Path.GetFileNameWithoutExtension(media);
        var fileName = options.NamingStrategy switch
        {
            SubtitleNamingStrategy.SameAsVideo => videoName + ".srt",
            SubtitleNamingStrategy.CustomTemplate => ExpandTemplate(options.NamingTemplate, videoName, options),
            _ => $"{videoName}.{options.Language}.{options.Layout}.srt"
        };
        ValidateFileName(fileName);
        if (!fileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)) fileName += ".srt";
        return Path.Combine(directory, fileName);
    }

    public async Task<SubtitlePublicationReceipt> PublishAndRecordAsync(
        SubtitlePublicationRequest request, CancellationToken cancellationToken)
    {
        SubtitlePublicationReceipt receipt;
        try
        {
            if (!File.Exists(request.SourceSubtitlePath))
                throw new FileNotFoundException("项目字幕不存在。", request.SourceSubtitlePath);
            if (!File.Exists(request.MediaPath))
                throw new FileNotFoundException("原视频已经移动或删除。", request.MediaPath);
            var target = BuildTargetPath(request);
            if (request.Options.Location != SubtitlePublishLocation.ProjectOnly &&
                !Path.GetFullPath(target).Equals(Path.GetFullPath(request.SourceSubtitlePath), StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                target = ResolveConflict(target, request.Options.ConflictPolicy);
                var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
                File.Copy(request.SourceSubtitlePath, temporary, true);
                File.Move(temporary, target, true);
            }
            receipt = new SubtitlePublicationReceipt(request, true, target,
                request.Options.Location == SubtitlePublishLocation.ProjectOnly ? "字幕保留在项目目录。" : $"字幕已发布到：{target}", DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            receipt = new SubtitlePublicationReceipt(request, false, null,
                $"字幕生成成功，但发布失败：{exception.Message}", DateTime.UtcNow);
        }
        await SaveReceiptAsync(receipt, cancellationToken);
        return receipt;
    }

    public async Task<SubtitlePublicationReceipt?> LoadReceiptAsync(string projectDirectory, CancellationToken cancellationToken)
    {
        var path = ReceiptPath(projectDirectory);
        if (!File.Exists(path)) return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<SubtitlePublicationReceipt>(stream, JsonOptions, cancellationToken);
    }

    public async Task<SubtitlePublicationReceipt> RepublishAsync(
        string projectDirectory, string? sourceSubtitlePath, CancellationToken cancellationToken)
    {
        var previous = await LoadReceiptAsync(projectDirectory, cancellationToken)
            ?? throw new InvalidOperationException("该项目还没有字幕发布记录。请先重新运行任务并选择输出策略。");
        var request = previous.Request with
        {
            SourceSubtitlePath = sourceSubtitlePath ?? previous.Request.SourceSubtitlePath,
            ProjectDirectory = projectDirectory
        };
        return await PublishAndRecordAsync(request, cancellationToken);
    }

    private async Task SaveReceiptAsync(SubtitlePublicationReceipt receipt, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(receipt.Request.ProjectDirectory);
        var path = ReceiptPath(receipt.Request.ProjectDirectory);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken);
        File.Move(temporary, path, true);
    }

    private static string ResolveConflict(string target, SubtitleConflictPolicy policy)
    {
        if (!File.Exists(target)) return target;
        if (policy == SubtitleConflictPolicy.BackupAndOverwrite)
        {
            var backup = target + ".pre-publish.bak";
            if (!File.Exists(backup)) File.Copy(target, backup);
            return target;
        }
        var directory = Path.GetDirectoryName(target)!;
        var name = Path.GetFileNameWithoutExtension(target);
        for (var index = 2; index < 10000; index++)
        {
            var candidate = Path.Combine(directory, $"{name} ({index}).srt");
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("无法为字幕生成不冲突的文件名。");
    }

    private static string ExpandTemplate(string template, string videoName, SubtitlePublicationOptions options)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("字幕命名模板不能为空。");
        var result = template.Replace("{video-name}", videoName, StringComparison.OrdinalIgnoreCase)
            .Replace("{language}", options.Language, StringComparison.OrdinalIgnoreCase)
            .Replace("{layout}", options.Layout, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
        if (result.Contains('{') || result.Contains('}')) throw new InvalidOperationException("字幕命名模板包含未知变量。");
        return result;
    }

    private static void ValidateFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Contains('/') || fileName.Contains('\\'))
            throw new InvalidOperationException("生成的字幕文件名为空或包含 Windows 不允许的字符。");
    }

    private static string ReceiptPath(string projectDirectory) =>
        Path.Combine(Path.GetFullPath(projectDirectory), "publication.json");
}
