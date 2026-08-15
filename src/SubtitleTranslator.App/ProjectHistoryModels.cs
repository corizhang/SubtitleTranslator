using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.App;

public sealed record ProjectStageItem(string Name, string State, DateTime UpdatedUtc, string Error);
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
}

public sealed class ProjectHistoryService
{
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
    public ObservableCollection<ProjectHistoryItem> Projects { get; } = [];
    public ProjectHistoryItem? SelectedProject { get => selectedProject; set { selectedProject = value; Notify(); Notify(nameof(HasSelection)); } }
    public bool HasSelection => SelectedProject is not null;
    public string Message { get => message; private set { message = value; Notify(); } }
    public ProjectHistoryService Service => service;
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task RefreshAsync()
    {
        var selectedPath = SelectedProject?.ProjectDirectory;
        Projects.Clear();
        foreach (var item in await service.LoadAsync(CancellationToken.None)) Projects.Add(item);
        SelectedProject = Projects.FirstOrDefault(x => x.ProjectDirectory == selectedPath) ?? Projects.FirstOrDefault();
        Message = Projects.Count == 0 ? "还没有项目。完成或中断一次字幕任务后会显示在这里。" : $"共 {Projects.Count} 个项目。";
    }

    public void SetMessage(string value) => Message = value;

    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
