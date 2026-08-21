using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;
using SubtitleTranslator.Media;
using System.Text.Json;
using System.Diagnostics;

namespace SubtitleTranslator.App;

public sealed record ProjectStageItem(string Name, string State, DateTime UpdatedUtc, string Error)
{
    public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("MM/dd HH:mm");
    public bool IsCompleted => State == "已完成";
}
public sealed record ProjectArtifactItem(string Name, string FullPath, string Kind, string SizeDisplay);

public sealed record ProjectHistoryItem(
    string ProjectDirectory,
    string Name,
    string SourcePath,
    string Status,
    int ProgressPercent,
    DateTime UpdatedUtc,
    long SizeBytes,
    IReadOnlyList<ProjectStageItem> Stages,
    IReadOnlyList<ProjectArtifactItem> Artifacts)
{
    public string UpdatedDisplay => UpdatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string SizeDisplay => SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{SizeBytes / 1024d / 1024 / 1024:0.0} GB",
        >= 1024L * 1024 => $"{SizeBytes / 1024d / 1024:0.0} MB",
        _ => $"{SizeBytes / 1024d:0} KB"
    };
    public bool SourceExists => File.Exists(SourcePath);
    public string SourceStateDisplay => SourceExists ? "原视频可用" : "原视频已移动或删除";
    public string MediaDetails { get; init; } = "正在读取媒体信息…";
    public string? ThumbnailPath { get; init; }
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);
    public string ResolutionDisplay => MediaDetailPart(0, "未知分辨率");
    public string DurationDisplay => MediaDetailPart(1, "未知时长");
    public string LibraryPrimaryActionText => Status == "已完成" ? "校订字幕" : ActionText;
    public string ActionText => Status switch
    {
        "已完成" => "查看",
        "处理中" => "查看进度",
        "失败，可恢复" => "重试",
        "已取消，可恢复" => "重新开始",
        _ => "继续"
    };
    private string MediaDetailPart(int index, string fallback)
    {
        var parts = MediaDetails.Split('丨', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > index ? parts[index] : fallback;
    }
}

public sealed class ProjectHistoryService
{
    private static readonly SemaphoreSlim ThumbnailWorkers = new(2);
    private sealed record ProjectMediaMetadata(long Length, DateTime LastWriteTimeUtc, int? Width, int? Height, TimeSpan Duration);
    public string ProjectsRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI字幕翻译", "projects");

    public async Task<IReadOnlyList<ProjectHistoryItem>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(ProjectsRoot)) return [];
        var result = new List<ProjectHistoryItem>();
        foreach (var directory in Directory.EnumerateDirectories(ProjectsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var manifest = await new FileProjectStore(directory).LoadAsync(cancellationToken);
                if (manifest is null) continue;
                var stages = manifest.Stages.Values.OrderBy(x => StageOrder(x.Stage)).Select(x =>
                    new ProjectStageItem(StageName(x.Stage), StateName(x.State), x.UpdatedUtc, x.Error ?? string.Empty)).ToArray();
                var completed = manifest.Stages.Values.Count(x => x.State == PipelineStageState.Completed);
                var status = OverallStatus(manifest);
                result.Add(new ProjectHistoryItem(directory, manifest.Name, manifest.Source.FullPath,
                    status, status == "已完成" ? 100 : Math.Min(99, completed * 100 / 7), manifest.UpdatedUtc,
                    DirectorySize(directory), stages, LoadArtifacts(directory)));
            }
            catch (Exception exception) { AppFileLogger.Error($"读取项目历史失败：{directory}", exception); }
        }
        return result.OrderByDescending(x => x.UpdatedUtc).ToArray();
    }

    public async Task<ProjectHistoryItem> EnrichMediaMetadataAsync(ProjectHistoryItem project, string ffprobePath, CancellationToken cancellationToken, string? ffmpegPath = null)
    {
        if (!project.SourceExists) return project with { MediaDetails = "原视频已移动或删除" };
        var source = new FileInfo(project.SourcePath);
        var cachePath = Path.Combine(project.ProjectDirectory, "media-metadata.json");
        ProjectMediaMetadata? metadata = null;
        try
        {
            if (File.Exists(cachePath))
            {
                await using var input = File.OpenRead(cachePath);
                var cached = await JsonSerializer.DeserializeAsync<ProjectMediaMetadata>(input, cancellationToken: cancellationToken);
                if (cached is not null && cached.Length == source.Length && cached.LastWriteTimeUtc == source.LastWriteTimeUtc)
                    metadata = cached;
            }
            if (metadata is null)
            {
                var media = await new FfprobeMediaProbe(ffprobePath).ProbeAsync(project.SourcePath, cancellationToken);
                metadata = new ProjectMediaMetadata(source.Length, source.LastWriteTimeUtc, media.VideoWidth, media.VideoHeight, media.Duration);
                Directory.CreateDirectory(project.ProjectDirectory);
                await using var output = File.Create(cachePath);
                await JsonSerializer.SerializeAsync(output, metadata, cancellationToken: cancellationToken);
            }
        }
        catch (Exception exception)
        {
            AppFileLogger.Info($"无法读取最近项目媒体信息：{exception.Message}");
            return project with { MediaDetails = "媒体信息不可用 丨 原视频可用" };
        }

        var resolution = metadata.Width > 0 && metadata.Height > 0 ? $"{metadata.Width}×{metadata.Height}" : "未知分辨率";
        var duration = metadata.Duration >= TimeSpan.FromHours(1)
            ? metadata.Duration.ToString("h\\:mm\\:ss")
            : metadata.Duration.ToString("mm\\:ss");
        var enriched = project with { MediaDetails = $"{resolution} 丨 {duration} 丨 原视频可用" };
        return string.IsNullOrWhiteSpace(ffmpegPath)
            ? enriched
            : enriched with { ThumbnailPath = await EnsureThumbnailAsync(enriched, ffmpegPath, metadata.Duration, cancellationToken) };
    }

    private static async Task<string?> EnsureThumbnailAsync(ProjectHistoryItem project, string ffmpegPath, TimeSpan duration, CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(project.ProjectDirectory, "cache", "thumbnails");
        var thumbnailPath = Path.Combine(cacheDirectory, "project-preview.jpg");
        var source = new FileInfo(project.SourcePath);
        if (File.Exists(thumbnailPath) && File.GetLastWriteTimeUtc(thumbnailPath) >= source.LastWriteTimeUtc) return thumbnailPath;

        await ThumbnailWorkers.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(thumbnailPath) && File.GetLastWriteTimeUtc(thumbnailPath) >= source.LastWriteTimeUtc) return thumbnailPath;
            Directory.CreateDirectory(cacheDirectory);
            var seek = TimeSpan.FromSeconds(Math.Clamp(duration.TotalSeconds * 0.1, 3, 60));
            var startInfo = new ProcessStartInfo(ffmpegPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-hide_banner"); startInfo.ArgumentList.Add("-loglevel"); startInfo.ArgumentList.Add("error");
            startInfo.ArgumentList.Add("-ss"); startInfo.ArgumentList.Add(seek.ToString("c"));
            startInfo.ArgumentList.Add("-i"); startInfo.ArgumentList.Add(project.SourcePath);
            startInfo.ArgumentList.Add("-frames:v"); startInfo.ArgumentList.Add("1");
            startInfo.ArgumentList.Add("-vf"); startInfo.ArgumentList.Add("scale=320:-2");
            startInfo.ArgumentList.Add("-q:v"); startInfo.ArgumentList.Add("3");
            startInfo.ArgumentList.Add("-y"); startInfo.ArgumentList.Add(thumbnailPath);
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            await process.WaitForExitAsync(cancellationToken);
            return process.ExitCode == 0 && File.Exists(thumbnailPath) ? thumbnailPath : null;
        }
        catch (Exception exception)
        {
            AppFileLogger.Info($"无法生成项目缩略图：{exception.Message}");
            return null;
        }
        finally { ThumbnailWorkers.Release(); }
    }

    public void DeleteCache(ProjectHistoryItem project)
    {
        var cache = SafeChild(project.ProjectDirectory, "cache");
        if (Directory.Exists(cache)) Directory.Delete(cache, true);
    }

    public void DeleteProject(ProjectHistoryItem project)
    {
        var full = Path.GetFullPath(project.ProjectDirectory);
        var root = Path.GetFullPath(ProjectsRoot);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("项目目录超出允许范围。");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    public Task<SubtitleTranslator.Application.SubtitlePublicationReceipt> RepublishAsync(
        ProjectHistoryItem project, CancellationToken cancellationToken) =>
        new SubtitlePublicationService().RepublishAsync(project.ProjectDirectory, null, cancellationToken);

    private static string SafeChild(string root, string name)
    {
        var fullRoot = Path.GetFullPath(root);
        var child = Path.GetFullPath(Path.Combine(fullRoot, name));
        if (!child.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("项目子目录超出允许范围。");
        return child;
    }

    private static long DirectorySize(string directory)
    {
        try { return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length); }
        catch { return 0; }
    }

    private static IReadOnlyList<ProjectArtifactItem> LoadArtifacts(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Where(path => Path.GetExtension(path).Equals(".srt", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetExtension(path).Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileName(path).Contains("qc", StringComparison.OrdinalIgnoreCase) ||
                               Path.GetFileName(path).Contains("report", StringComparison.OrdinalIgnoreCase))
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    var kind = path.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ass", StringComparison.OrdinalIgnoreCase) ? "字幕" : "报告";
                    var size = info.Length >= 1024 * 1024 ? $"{info.Length / 1024d / 1024:0.0} MB" : $"{info.Length / 1024d:0} KB";
                    return new ProjectArtifactItem(info.Name, path, kind, size);
                }).OrderByDescending(x => x.Kind == "字幕").ThenBy(x => x.Name).ToArray();
        }
        catch { return []; }
    }

    private static string OverallStatus(SubtitleProjectManifest project)
    {
        var states = project.Stages.Values.Select(x => x.State).ToArray();
        if (states.Contains(PipelineStageState.Running)) return "处理中";
        if (states.Contains(PipelineStageState.Failed)) return "失败，可恢复";
        if (states.Contains(PipelineStageState.Cancelled)) return "已取消，可恢复";
        return states.Length > 0 && states.All(x => x == PipelineStageState.Completed) ? "已完成" : "可继续";
    }

    private static int StageOrder(string stage) => stage switch
    { "probe" => 0, "audio" => 1, "transcription" => 2, "translation" => 3, "translation-qa" => 4, "final-qc" => 5, "export" => 6, _ => 99 };
    private static string StageName(string stage) => stage switch
    { "probe" => "读取媒体", "audio" => "提取音频", "transcription" => "语音识别", "translation" => "翻译", "translation-qa" => "翻译 QA", "final-qc" => "最终质检", "export" => "导出字幕", _ => stage };
    private static string StateName(PipelineStageState state) => state switch
    { PipelineStageState.Completed => "已完成", PipelineStageState.Running => "处理中", PipelineStageState.Failed => "失败", PipelineStageState.Cancelled => "已取消", _ => "等待" };
}

public sealed class ProjectHistoryViewModel : INotifyPropertyChanged
{
    private readonly ProjectHistoryService service = new();
    private ProjectHistoryItem? selectedProject;
    private string message = "正在读取项目历史……";
    private string searchText = string.Empty;
    private string statusFilter = "全部状态";
    public ProjectHistoryViewModel()
    {
        ProjectsView = CollectionViewSource.GetDefaultView(Projects);
        ProjectsView.Filter = MatchesFilter;
    }
    public ObservableCollection<ProjectHistoryItem> Projects { get; } = [];
    public ICollectionView ProjectsView { get; }
    public IReadOnlyList<string> StatusFilters { get; } = ["全部状态", "可继续或恢复", "已完成", "源文件缺失"];
    public ProjectHistoryItem? SelectedProject { get => selectedProject; set { selectedProject = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => SelectedProject is not null;
    public bool HasProjects => Projects.Count > 0;
    public int FilteredCount => ProjectsView.Cast<object>().Count();
    public bool HasVisibleProjects => FilteredCount > 0;
    public string EmptyStateTitle => HasProjects ? "没有匹配的项目" : "还没有字幕项目";
    public string EmptyStateDescription => HasProjects ? "尝试清空搜索内容或选择其他状态。" : "从工作台选择视频并开始处理后，项目会自动保存在这里。";
    public int TotalCount => Projects.Count;
    public int CompletedCount => Projects.Count(x => x.Status == "已完成");
    public int RecoverableCount => Projects.Count(x => x.Status != "已完成");
    public int MissingSourceCount => Projects.Count(x => !x.SourceExists);
    public string ProjectSummaryDisplay => $"共 {TotalCount} 个项目  ·  {CompletedCount} 个已完成  ·  占用 {TotalSizeDisplay}";
    public string TotalSizeDisplay
    {
        get
        {
            var bytes = Projects.Sum(x => x.SizeBytes);
            return bytes switch
            {
                >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024 / 1024:0.0} GB",
                >= 1024L * 1024 => $"{bytes / 1024d / 1024:0} MB",
                _ => $"{bytes / 1024d:0} KB"
            };
        }
    }
    public string SearchText { get => searchText; set { if (searchText == value) return; searchText = value; Notify(); RefreshFilter(); } }
    public string StatusFilter { get => statusFilter; set { if (statusFilter == value) return; statusFilter = value; Notify(); RefreshFilter(); } }
    public string Message { get => message; private set { message = value; Notify(); } }
    public ProjectHistoryService Service => service;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshAsync(string? ffprobePath = null, string? ffmpegPath = null)
    {
        var selectedPath = SelectedProject?.ProjectDirectory;
        Projects.Clear();
        var loaded = await service.LoadAsync(CancellationToken.None);
        var enriched = string.IsNullOrWhiteSpace(ffprobePath)
            ? loaded
            : await Task.WhenAll(loaded.Select(item => service.EnrichMediaMetadataAsync(item, ffprobePath, CancellationToken.None, ffmpegPath)));
        foreach (var item in enriched) Projects.Add(item);
        Notify(nameof(HasProjects)); Notify(nameof(TotalCount)); Notify(nameof(CompletedCount));
        Notify(nameof(RecoverableCount)); Notify(nameof(MissingSourceCount));
        Notify(nameof(TotalSizeDisplay)); Notify(nameof(ProjectSummaryDisplay));
        ProjectsView.Refresh();
        SelectedProject = ProjectsView.Cast<ProjectHistoryItem>().FirstOrDefault(x => x.ProjectDirectory == selectedPath)
            ?? ProjectsView.Cast<ProjectHistoryItem>().FirstOrDefault();
        RefreshFilter();
    }

    public void SetMessage(string value) => Message = value;

    private bool MatchesFilter(object value)
    {
        if (value is not ProjectHistoryItem project) return false;
        var matchesText = string.IsNullOrWhiteSpace(SearchText) ||
            project.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
            project.SourcePath.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        var matchesStatus = StatusFilter switch
        {
            "可继续或恢复" => project.Status != "已完成",
            "已完成" => project.Status == "已完成",
            "源文件缺失" => !project.SourceExists,
            _ => true
        };
        return matchesText && matchesStatus;
    }

    private void RefreshFilter()
    {
        ProjectsView.Refresh();
        var visible = FilteredCount;
        Message = Projects.Count == 0 ? "还没有项目。完成或中断一次字幕任务后会显示在这里。" : $"显示 {visible} 个，共 {Projects.Count} 个项目。";
        Notify(nameof(FilteredCount)); Notify(nameof(HasVisibleProjects));
        Notify(nameof(EmptyStateTitle)); Notify(nameof(EmptyStateDescription));
        if (SelectedProject is not null && !ProjectsView.Cast<ProjectHistoryItem>().Contains(SelectedProject))
            SelectedProject = ProjectsView.Cast<ProjectHistoryItem>().FirstOrDefault();
    }

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
