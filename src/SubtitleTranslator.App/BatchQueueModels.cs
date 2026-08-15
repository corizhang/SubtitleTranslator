using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.App;

public sealed class BatchQueueItemViewModel : INotifyPropertyChanged
{
    private BatchTaskState state;
    private double progress;
    private string stage;
    private string? error;
    private string? subtitlePath;

    public BatchQueueItemViewModel(BatchQueueEntry entry)
    {
        Id = entry.Id; MediaPath = entry.MediaPath; state = entry.State; progress = entry.Progress;
        stage = entry.Stage; error = entry.Error; subtitlePath = entry.SubtitlePath; UpdatedUtc = entry.UpdatedUtc;
    }

    public Guid Id { get; }
    public string MediaPath { get; }
    public string Name => Path.GetFileNameWithoutExtension(MediaPath);
    public bool SourceExists => File.Exists(MediaPath);
    public bool CanProcess => SourceExists && BatchQueueViewModel.Extensions.Contains(Path.GetExtension(MediaPath));
    public string PreflightDisplay => !SourceExists ? "文件缺失" : CanProcess ? "可以处理" : "格式不支持";
    public string FileSizeDisplay
    {
        get
        {
            if (!SourceExists) return "—";
            var bytes = new FileInfo(MediaPath).Length;
            return bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1024d / 1024 / 1024:0.0} GB" : $"{bytes / 1024d / 1024:0} MB";
        }
    }
    public BatchTaskState State { get => state; set { Set(ref state, value); Notify(nameof(StateDisplay)); } }
    public string StateDisplay => State switch
    { BatchTaskState.Pending => "等待", BatchTaskState.Running => "处理中", BatchTaskState.Completed => "完成", BatchTaskState.Failed => "失败", _ => "已取消" };
    public double Progress { get => progress; set => Set(ref progress, value); }
    public string Stage { get => stage; set => Set(ref stage, value); }
    public string? Error { get => error; set => Set(ref error, value); }
    public string? SubtitlePath { get => subtitlePath; set { Set(ref subtitlePath, value); Notify(nameof(CanOpenSubtitle)); } }
    public bool CanOpenSubtitle => SubtitlePath is not null && File.Exists(SubtitlePath);
    public DateTime UpdatedUtc { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public BatchQueueEntry ToEntry() => new(Id, MediaPath, State, Progress, Stage, Error, SubtitlePath, UpdatedUtc);
    public void RefreshPreflight()
    {
        Notify(nameof(SourceExists));
        Notify(nameof(CanProcess));
        Notify(nameof(PreflightDisplay));
        Notify(nameof(FileSizeDisplay));
    }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; Notify(name); } }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class BatchQueueViewModel : INotifyPropertyChanged
{
    internal static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v" };
    private readonly MainWindowViewModel main;
    private readonly JsonBatchQueueStore store;
    private CancellationTokenSource? cancellation;
    private BatchQueueItemViewModel? selectedItem;
    private bool isRunning;
    private string message = "添加多个视频后即可开始顺序处理。";

    public BatchQueueViewModel(MainWindowViewModel main)
    {
        this.main = main;
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI字幕翻译", "batch-queue.json");
        store = new JsonBatchQueueStore(path);
    }

    public ObservableCollection<BatchQueueItemViewModel> Items { get; } = [];
    public BatchQueueItemViewModel? SelectedItem { get => selectedItem; set { selectedItem = value; Notify(); } }
    public bool IsRunning { get => isRunning; private set { isRunning = value; Notify(); Notify(nameof(CanEdit)); } }
    public bool CanEdit => !IsRunning;
    public string Message { get => message; private set { message = value; Notify(); } }
    public int TotalCount => Items.Count;
    public int ReadyCount => Items.Count(x => x.CanProcess && x.State == BatchTaskState.Pending);
    public int NeedsAttentionCount => Items.Count(x => !x.CanProcess || x.State is BatchTaskState.Failed or BatchTaskState.Cancelled);
    public int CompletedCount => Items.Count(x => x.State == BatchTaskState.Completed);
    public double QueueProgress => Items.Count == 0 ? 0 : Items.Average(x => x.Progress);
    public string QueueProgressDisplay => $"{QueueProgress:0}%";
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadAsync()
    {
        Items.Clear();
        var snapshot = await store.LoadAsync(CancellationToken.None);
        foreach (var entry in snapshot.Items) Items.Add(new BatchQueueItemViewModel(entry));
        SelectedItem = Items.FirstOrDefault();
        UpdateSummary();
    }

    public async Task AddFilesAsync(IEnumerable<string> paths)
    {
        var existing = Items.Select(x => x.MediaPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(File.Exists))
        {
            var full = Path.GetFullPath(path);
            if (!Extensions.Contains(Path.GetExtension(full)) || !existing.Add(full)) continue;
            Items.Add(new BatchQueueItemViewModel(new BatchQueueEntry(
                Guid.NewGuid(), full, BatchTaskState.Pending, 0, "等待处理", null, null, DateTime.UtcNow)));
        }
        SelectedItem ??= Items.FirstOrDefault();
        await SaveAsync();
        UpdateSummary();
    }

    public async Task RemoveSelectedAsync()
    {
        if (IsRunning || SelectedItem is null) return;
        Items.Remove(SelectedItem);
        SelectedItem = Items.FirstOrDefault();
        await SaveAsync();
        UpdateSummary();
    }

    public async Task RerunPreflightAsync()
    {
        foreach (var item in Items) item.RefreshPreflight();
        UpdateSummary();
        await SaveAsync();
    }

    public async Task StartAsync(bool retryFailed = false)
    {
        if (IsRunning) return;
        if (retryFailed)
            foreach (var item in Items.Where(x => x.State is BatchTaskState.Failed or BatchTaskState.Cancelled))
            { item.State = BatchTaskState.Pending; item.Progress = 0; item.Stage = "等待从缓存断点继续"; item.Error = null; }
        var pending = Items.Where(x => x.State == BatchTaskState.Pending && x.CanProcess).ToArray();
        if (pending.Length == 0) { Message = "没有通过预检且等待处理的任务。请移除或修正标记为文件缺失的项目。"; return; }
        try { await main.PrepareBatchAsync(); }
        catch (Exception exception) { Message = exception.Message; return; }

        cancellation = new CancellationTokenSource();
        IsRunning = true;
        main.BeginExternalTask(pending[0].MediaPath, Cancel);
        try
        {
            foreach (var item in pending)
            {
                if (cancellation.IsCancellationRequested) break;
                SelectedItem = item;
                main.BeginExternalTask(item.MediaPath, Cancel);
                item.State = BatchTaskState.Running; item.Stage = "准备任务"; item.Error = null; item.UpdatedUtc = DateTime.UtcNow;
                await SaveAsync(); UpdateSummary();
                var progress = new Progress<PipelineProgress>(value =>
                {
                    item.Stage = StageName(value.Stage); item.Progress = Overall(value.Stage, value.Percent);
                    main.ReportExternalTask(item.MediaPath, value);
                    NotifyQueueSummary();
                });
                try
                {
                    var result = await main.RunBatchItemAsync(item.MediaPath, progress, cancellation.Token);
                    item.State = BatchTaskState.Completed; item.Progress = 100; item.Stage = result.Message;
                    item.SubtitlePath = result.SubtitlePath;
                    main.ReportExternalItemResult(item.MediaPath, true, result.Message);
                }
                catch (OperationCanceledException)
                {
                    item.State = BatchTaskState.Cancelled; item.Stage = "已取消，可重试";
                    main.ReportExternalItemResult(item.MediaPath, false, "已取消，可从有效缓存继续");
                }
                catch (Exception exception)
                {
                    item.State = BatchTaskState.Failed; item.Stage = "失败，可重试"; item.Error = exception.Message;
                    main.ReportExternalItemResult(item.MediaPath, false, exception.Message);
                }
                item.UpdatedUtc = DateTime.UtcNow;
                await SaveAsync(); UpdateSummary();
            }
        }
        finally
        {
            cancellation.Dispose(); cancellation = null; IsRunning = false; UpdateSummary();
            await main.EndExternalTaskAsync();
        }
    }

    public void Cancel() => cancellation?.Cancel();
    public void OpenSubtitle()
    {
        if (SelectedItem?.CanOpenSubtitle == true)
            Process.Start(new ProcessStartInfo(SelectedItem.SubtitlePath!) { UseShellExecute = true });
    }

    private Task SaveAsync() => store.SaveAsync(new BatchQueueSnapshot(1, Items.Select(x => x.ToEntry()).ToArray()), CancellationToken.None);
    private void UpdateSummary()
    {
        Message = $"共 {Items.Count} 项：可处理 {Items.Count(x => x.CanProcess)}，需修正 {Items.Count(x => !x.CanProcess)}，处理中 {Items.Count(x => x.State == BatchTaskState.Running)}，完成 {Items.Count(x => x.State == BatchTaskState.Completed)}，失败/取消 {Items.Count(x => x.State is BatchTaskState.Failed or BatchTaskState.Cancelled)}。";
        NotifyQueueSummary();
    }
    private void NotifyQueueSummary()
    {
        Notify(nameof(TotalCount));
        Notify(nameof(ReadyCount));
        Notify(nameof(NeedsAttentionCount));
        Notify(nameof(CompletedCount));
        Notify(nameof(QueueProgress));
        Notify(nameof(QueueProgressDisplay));
    }
    private static string StageName(string stage) => stage switch
    { "probe" => "读取媒体", "audio" => "提取音频", "transcription" or "transcribe" => "语音识别", "translation" => "翻译", "translation-qa" => "翻译 QA", "final-qc" => "最终质检", "export" => "导出字幕", _ => "处理中" };
    private static double Overall(string stage, double? percent)
    {
        var local = Math.Clamp(percent ?? 0, 0, 100) / 100d;
        var (start, weight) = stage switch
        { "probe" => (0d, 2d), "audio" => (2d, 8d), "transcription" or "transcribe" => (18d, 47d), "translation" => (65d, 20d), "translation-qa" => (85d, 8d), "final-qc" => (93d, 4d), "export" => (97d, 3d), _ => (0d, 0d) };
        return Math.Min(100, start + weight * local);
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
