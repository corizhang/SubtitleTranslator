namespace SubtitleTranslator.Application;

public sealed record UserSettings(
    int SchemaVersion = 1,
    string? WhisperModelPath = null,
    string? VadModelPath = null,
    string? FfmpegPath = null,
    string? FfprobePath = null,
    string? WhisperRuntimePath = null,
    string? VlcRuntimePath = null,
    string DeepSeekModel = "deepseek-v4-flash",
    SubtitlePublishLocation SubtitlePublishLocation = SubtitlePublishLocation.VideoDirectory,
    SubtitleNamingStrategy SubtitleNamingStrategy = SubtitleNamingStrategy.VideoNameWithTags,
    SubtitleConflictPolicy SubtitleConflictPolicy = SubtitleConflictPolicy.BackupAndOverwrite,
    string? SubtitleCustomDirectory = null,
    string SubtitleNamingTemplate = "{video-name}.{language}.{layout}.srt",
    string DefaultOutputMode = "中文 + 原语言双字幕",
    string DefaultQualityMode = "自动（推荐）",
    string DefaultSourceLanguage = "自动检测",
    bool DefaultTranslationQaEnabled = true);

public enum SubtitlePublishLocation { VideoDirectory, CustomDirectory, ProjectOnly }
public enum SubtitleNamingStrategy { VideoNameWithTags, SameAsVideo, CustomTemplate }
public enum SubtitleConflictPolicy { BackupAndOverwrite, AutoNumber }

public sealed record SubtitlePublicationOptions(
    SubtitlePublishLocation Location = SubtitlePublishLocation.VideoDirectory,
    SubtitleNamingStrategy NamingStrategy = SubtitleNamingStrategy.VideoNameWithTags,
    SubtitleConflictPolicy ConflictPolicy = SubtitleConflictPolicy.BackupAndOverwrite,
    string? CustomDirectory = null,
    string NamingTemplate = "{video-name}.{language}.{layout}.srt",
    string Language = "zh-CN",
    string Layout = "bilingual");

public sealed record SubtitlePublicationRequest(
    string MediaPath,
    string SourceSubtitlePath,
    string ProjectDirectory,
    SubtitlePublicationOptions Options);

public sealed record SubtitlePublicationReceipt(
    SubtitlePublicationRequest Request,
    bool Success,
    string? PublishedPath,
    string Message,
    DateTime UpdatedUtc);

public sealed record DownloadableModel(
    string Id,
    string DisplayName,
    string FileName,
    long SizeBytes,
    string Sha256,
    Uri DownloadUri,
    string Description);

public sealed record ModelDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    double Percent,
    string Message);

public enum ComponentState { Ready, Missing, Invalid, Optional }

public sealed record ComponentDiagnostic(
    string Id,
    string DisplayName,
    ComponentState State,
    string Message,
    string? ResolvedPath = null);

public sealed record EnvironmentDiagnosticReport(
    IReadOnlyList<ComponentDiagnostic> Components)
{
    public bool CanGenerateSubtitles => Components
        .Where(x => x.Id is "ffmpeg" or "ffprobe" or "whisper-model" or "whisper-runtime" or "vad")
        .All(x => x.State == ComponentState.Ready);
}

public enum ComponentArchiveType { RawFile, Zip }

public sealed record DownloadableComponent(
    string Id,
    string DisplayName,
    string Version,
    string DownloadFileName,
    long DownloadSizeBytes,
    string Sha256,
    Uri DownloadUri,
    ComponentArchiveType ArchiveType,
    string InstallDirectoryName,
    string? ZipEntryPrefix,
    string RequiredRelativePath);

public sealed record ComponentInstallResult(
    string ComponentId,
    string InstallDirectory,
    string RequiredPath);

public sealed record HardwareDiagnosticReport(
    bool HasNvidiaGpu,
    string? GpuName,
    string? DriverVersion,
    string? ComputeCapability,
    bool HasCudaToolkit,
    string? CudaToolkitVersion,
    bool HasWhisperRuntime,
    string RuntimeKind,
    IReadOnlyList<string> Warnings);

public sealed record WhisperSelfTestResult(
    bool Success,
    TimeSpan Elapsed,
    string RuntimeKind,
    string Message);
