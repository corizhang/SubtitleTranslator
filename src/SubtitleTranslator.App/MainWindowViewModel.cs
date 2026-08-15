using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Input;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Orchestration;
using SubtitleTranslator.Infrastructure;
using SubtitleTranslator.Media;
using SubtitleTranslator.Speech;

namespace SubtitleTranslator.App;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v" };

    private readonly string workspaceRoot;
    private readonly ISubtitleGenerationService generationService;
    private readonly IUserSettingsStore settingsStore;
    private readonly IModelDownloadService modelDownloadService;
    private readonly ISecretStore secretStore;
    private readonly IEnvironmentDiagnosticService environmentDiagnosticService;
    private readonly IComponentInstallService componentInstallService;
    private readonly IHardwareDiagnosticService hardwareDiagnosticService;
    private readonly IWhisperRuntimeSelfTestService selfTestService;
    private readonly SubtitlePublicationService publicationService = new();
    private UserSettings settings;
    private string? deepSeekApiKey;
    private CancellationTokenSource? runCancellation;
    private CancellationTokenSource? modelDownloadCancellation;
    private CancellationTokenSource? componentInstallCancellation;
    private CancellationTokenSource? selfTestCancellation;
    private string? selectedFilePath;
    private string selectedOutputMode = "中文 + 原语言双字幕";
    private string selectedQualityMode = "自动（推荐）";
    private string selectedSpeechModel = "Large v3 Turbo Q5（推荐）";
    private string selectedTranslationProvider = "DeepSeek";
    private string selectedSourceLanguage = "自动检测";
    private AudioTrackOption? selectedAudioTrack;
    private bool translationQaEnabled = true;
    private bool isRunning;
    private double overallProgress;
    private string currentStage = "等待开始";
    private string? resultSubtitlePath;
    private string? resultOutputDirectory;
    private string? resultProjectDirectory;
    private string? selectedModelPath;
    private string modelStatus = "尚未选择模型。";
    private bool isModelDownloading;
    private double modelDownloadProgress;
    private string apiKeyStatus = "尚未配置 DeepSeek API Key。";
    private string deepSeekConnectionStatus = "保存密钥后可测试连接。";
    private bool isTestingDeepSeek;
    private string environmentStatus = "正在检测运行组件……";
    private EnvironmentDiagnosticReport? environmentReport;
    private bool isComponentInstalling;
    private double componentInstallProgress;
    private string componentInstallStatus = "可选择本地组件，或按需下载安装。";
    private string hardwareStatus = "正在检测 GPU 与 CUDA 环境……";
    private string selfTestStatus = "安装或选择运行组件后，可执行本地推理自检。";
    private bool isSelfTesting;
    private string validationMessage = "请先拖入或选择一个视频文件。";
    private string statusMessage = "准备就绪。";
    private string selectedPublishLocation = "视频所在目录（推荐）";
    private string selectedNamingStrategy = "视频名 + 语言和类型（推荐）";
    private string selectedConflictPolicy = "覆盖前备份（推荐）";
    private string customOutputDirectory = string.Empty;
    private string namingTemplate = "{video-name}.{language}.{layout}.srt";

    public MainWindowViewModel()
    {
        workspaceRoot = FindWorkspaceRoot();
        generationService = new InProcessSubtitleGenerationService(workspaceRoot);
        var userDataRoot = GetUserDataRoot();
        settingsStore = new JsonUserSettingsStore(Path.Combine(userDataRoot, "settings.json"));
        secretStore = new WindowsDpapiSecretStore(Path.Combine(userDataRoot, "secrets"));
        modelDownloadService = new HttpModelDownloadService(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        environmentDiagnosticService = new EnvironmentDiagnosticService();
        componentInstallService = new ComponentInstallService(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        hardwareDiagnosticService = new WindowsHardwareDiagnosticService();
        selfTestService = new WhisperRuntimeSelfTestService();
        // The view model is constructed on WPF's dispatcher thread. Run initial async I/O on
        // the thread pool so an existing settings/secret file cannot deadlock the UI context.
        settings = Task.Run(() => settingsStore.LoadAsync(CancellationToken.None)).GetAwaiter().GetResult();
        deepSeekApiKey = Task.Run(() => secretStore.ReadAsync("deepseek-api-key", CancellationToken.None)).GetAwaiter().GetResult();
        apiKeyStatus = string.IsNullOrWhiteSpace(deepSeekApiKey)
            ? "尚未配置 DeepSeek API Key。" : "DeepSeek API Key 已加密保存（当前 Windows 用户）。";
        selectedModelPath = ResolveInitialModel(settings.WhisperModelPath);
        settings = ApplyDevelopmentFallbacks(settings, selectedModelPath);
        selectedPublishLocation = PublishLocationName(settings.SubtitlePublishLocation);
        selectedNamingStrategy = NamingStrategyName(settings.SubtitleNamingStrategy);
        selectedConflictPolicy = ConflictPolicyName(settings.SubtitleConflictPolicy);
        customOutputDirectory = settings.SubtitleCustomDirectory ?? string.Empty;
        namingTemplate = settings.SubtitleNamingTemplate;
        modelStatus = DescribeModel(selectedModelPath);
        StartCommand = new AsyncRelayCommand(StartAsync, CanStart);
        CancelCommand = new RelayCommand(Cancel, () => IsRunning);
        DownloadModelCommand = new AsyncRelayCommand(DownloadSelectedModelAsync, () => !IsRunning && !IsModelDownloading);
        CancelModelDownloadCommand = new RelayCommand(() => modelDownloadCancellation?.Cancel(), () => IsModelDownloading);
        DeleteApiKeyCommand = new AsyncRelayCommand(DeleteApiKeyAsync, () => deepSeekApiKey is not null && !IsRunning);
        TestDeepSeekConnectionCommand = new AsyncRelayCommand(TestDeepSeekConnectionAsync,
            () => HasSavedApiKey && !IsRunning && !IsTestingDeepSeek);
        RefreshEnvironmentCommand = new AsyncRelayCommand(RefreshEnvironmentAsync, () => !IsRunning);
        InstallVadCommand = new AsyncRelayCommand(() => InstallComponentAsync(ComponentCatalog.Vad), CanInstallComponent);
        InstallCpuRuntimeCommand = new AsyncRelayCommand(() => InstallComponentAsync(ComponentCatalog.CpuRuntime), CanInstallComponent);
        InstallCudaRuntimeCommand = new AsyncRelayCommand(() => InstallComponentAsync(ComponentCatalog.CudaRuntime), CanInstallComponent);
        CancelComponentInstallCommand = new RelayCommand(() => componentInstallCancellation?.Cancel(), () => IsComponentInstalling);
        OpenFfmpegDownloadCommand = new RelayCommand(OpenFfmpegDownloadPage);
        RunSelfTestCommand = new AsyncRelayCommand(RunSelfTestAsync, CanRunSelfTest);
        OpenSubtitleCommand = new RelayCommand(OpenSubtitle, () => HasResult);
        OpenOutputFolderCommand = new RelayCommand(OpenOutputFolder, () => HasResult);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public IReadOnlyList<string> OutputModes { get; } = ["中文 + 原语言双字幕", "仅中文字幕"];
    public IReadOnlyList<string> QualityModes { get; } = ["自动（推荐）", "生成建议清单", "关闭"];
    public IReadOnlyList<string> SpeechModels { get; } = ["Large v3 Turbo Q5（推荐）", "Small Q5（速度优先）"];
    public IReadOnlyList<string> TranslationProviders { get; } = ["DeepSeek"];
    public IReadOnlyList<string> SourceLanguages { get; } = ["自动检测", "英语", "日语", "韩语", "法语", "德语", "西班牙语", "俄语"];
    public IReadOnlyList<string> PublishLocations { get; } = ["视频所在目录（推荐）", "自定义目录", "仅项目目录"];
    public IReadOnlyList<string> NamingStrategies { get; } = ["视频名 + 语言和类型（推荐）", "与视频完全同名", "自定义模板"];
    public IReadOnlyList<string> ConflictPolicies { get; } = ["覆盖前备份（推荐）", "自动编号"];
    public List<AudioTrackOption> AudioTracks { get; } = [];
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenSubtitleCommand { get; }
    public ICommand OpenOutputFolderCommand { get; }
    public ICommand DownloadModelCommand { get; }
    public ICommand CancelModelDownloadCommand { get; }
    public ICommand DeleteApiKeyCommand { get; }
    public ICommand TestDeepSeekConnectionCommand { get; }
    public ICommand RefreshEnvironmentCommand { get; }
    public ICommand InstallVadCommand { get; }
    public ICommand InstallCpuRuntimeCommand { get; }
    public ICommand InstallCudaRuntimeCommand { get; }
    public ICommand CancelComponentInstallCommand { get; }
    public ICommand OpenFfmpegDownloadCommand { get; }
    public ICommand RunSelfTestCommand { get; }

    public string? SelectedFilePath { get => selectedFilePath; private set { selectedFilePath = value; Notify(); Notify(nameof(SelectedFileDisplay)); } }
    public string SelectedFileDisplay => SelectedFilePath ?? "支持 MKV、MP4、AVI、MOV、WMV、WebM";
    public string SelectedOutputMode { get => selectedOutputMode; set => Set(ref selectedOutputMode, value); }
    public string SelectedQualityMode { get => selectedQualityMode; set => Set(ref selectedQualityMode, value); }
    public string SelectedSpeechModel { get => selectedSpeechModel; set { Set(ref selectedSpeechModel, value); RefreshCommands(); } }
    public string SelectedTranslationProvider { get => selectedTranslationProvider; set => Set(ref selectedTranslationProvider, value); }
    public string SelectedSourceLanguage { get => selectedSourceLanguage; set => Set(ref selectedSourceLanguage, value); }
    public AudioTrackOption? SelectedAudioTrack { get => selectedAudioTrack; set => Set(ref selectedAudioTrack, value); }
    public bool TranslationQaEnabled { get => translationQaEnabled; set => Set(ref translationQaEnabled, value); }
    public bool IsRunning { get => isRunning; private set { Set(ref isRunning, value); RefreshCommands(); } }
    public string ValidationMessage { get => validationMessage; private set => Set(ref validationMessage, value); }
    public string StatusMessage { get => statusMessage; private set => Set(ref statusMessage, value); }
    public double OverallProgress { get => overallProgress; private set => Set(ref overallProgress, value); }
    public string CurrentStage { get => currentStage; private set => Set(ref currentStage, value); }
    public string? ResultSubtitlePath { get => resultSubtitlePath; private set { Set(ref resultSubtitlePath, value); Notify(nameof(HasResult)); RefreshCommands(); } }
    public bool HasResult => ResultSubtitlePath is not null && File.Exists(ResultSubtitlePath);
    public string ModelStatus { get => modelStatus; private set => Set(ref modelStatus, value); }
    public bool IsModelDownloading { get => isModelDownloading; private set { Set(ref isModelDownloading, value); RefreshCommands(); } }
    public double ModelDownloadProgress { get => modelDownloadProgress; private set => Set(ref modelDownloadProgress, value); }
    public string ApiKeyStatus { get => apiKeyStatus; private set => Set(ref apiKeyStatus, value); }
    public string DeepSeekConnectionStatus { get => deepSeekConnectionStatus; private set => Set(ref deepSeekConnectionStatus, value); }
    public bool IsTestingDeepSeek { get => isTestingDeepSeek; private set { Set(ref isTestingDeepSeek, value); RefreshCommands(); } }
    public string EnvironmentStatus { get => environmentStatus; private set => Set(ref environmentStatus, value); }
    public bool IsComponentInstalling { get => isComponentInstalling; private set { Set(ref isComponentInstalling, value); RefreshCommands(); } }
    public double ComponentInstallProgress { get => componentInstallProgress; private set => Set(ref componentInstallProgress, value); }
    public string ComponentInstallStatus { get => componentInstallStatus; private set => Set(ref componentInstallStatus, value); }
    public string HardwareStatus { get => hardwareStatus; private set => Set(ref hardwareStatus, value); }
    public string SelfTestStatus { get => selfTestStatus; private set => Set(ref selfTestStatus, value); }
    public bool IsSelfTesting { get => isSelfTesting; private set { Set(ref isSelfTesting, value); RefreshCommands(); } }
    public bool HasSavedApiKey => !string.IsNullOrWhiteSpace(deepSeekApiKey);
    public bool NeedsInitialSetup => environmentReport?.CanGenerateSubtitles != true || !HasSavedApiKey;
    public string SelectedPublishLocation { get => selectedPublishLocation; set { Set(ref selectedPublishLocation, value); Notify(nameof(PublicationPreview)); } }
    public string SelectedNamingStrategy { get => selectedNamingStrategy; set { Set(ref selectedNamingStrategy, value); Notify(nameof(PublicationPreview)); } }
    public string SelectedConflictPolicy { get => selectedConflictPolicy; set => Set(ref selectedConflictPolicy, value); }
    public string CustomOutputDirectory { get => customOutputDirectory; private set { Set(ref customOutputDirectory, value); Notify(nameof(PublicationPreview)); } }
    public string NamingTemplate { get => namingTemplate; set { Set(ref namingTemplate, value); Notify(nameof(PublicationPreview)); } }
    public string PublicationPreview => BuildPublicationPreview();

    public Task InitializeAsync() => RefreshEnvironmentAsync();

    public bool IsComponentReady(string id) => environmentReport?.Components
        .Any(x => x.Id == id && x.State == ComponentState.Ready) == true;

    public bool IsSetupStepComplete(int step) => step switch
    {
        0 => true,
        1 => IsComponentReady("ffmpeg") && IsComponentReady("ffprobe"),
        2 => IsComponentReady("whisper-runtime") && IsComponentReady("vad"),
        3 => IsComponentReady("whisper-model"),
        4 => HasSavedApiKey,
        _ => false
    };

    public string SetupSummary => $"{EnvironmentStatus}\n{HardwareStatus}\n{ApiKeyStatus}";

    public async Task<string?> ValidateSetupStepAsync(int step)
    {
        await RefreshEnvironmentAsync();
        return step switch
        {
            0 => null, // GPU is optional; CPU processing remains available.
            1 when !IsComponentReady("ffmpeg") || !IsComponentReady("ffprobe")
                => "请先选择有效的 FFmpeg；FFprobe 通常应与 ffmpeg.exe 位于同一目录。",
            2 when !IsComponentReady("whisper-runtime") || !IsComponentReady("vad")
                => "请先配置 Whisper 运行组件和 Silero VAD 模型。",
            3 when !IsComponentReady("whisper-model")
                => "请先选择或下载一个 Whisper 模型。",
            4 when !HasSavedApiKey
                => "请先保存 DeepSeek API Key。密钥只会加密保存在当前 Windows 用户目录。",
            _ => null
        };
    }

    public async Task SelectVideoAsync(string path)
    {
        if (IsRunning) return;
        if (!File.Exists(path)) { ValidationMessage = "所选文件不存在。"; return; }
        if (!SupportedExtensions.Contains(Path.GetExtension(path))) { ValidationMessage = "暂不支持此文件格式。"; return; }
        SelectedFilePath = Path.GetFullPath(path);
        Notify(nameof(PublicationPreview));
        ValidationMessage = string.Empty;
        StatusMessage = $"已选择：{Path.GetFileName(path)}。";
        CurrentStage = "等待开始";
        OverallProgress = 0;
        ResultSubtitlePath = null;
        resultOutputDirectory = null;
        resultProjectDirectory = null;
        RefreshCommands();
        await LoadAudioTracksAsync();
    }

    private async Task LoadAudioTracksAsync()
    {
        AudioTracks.Clear();
        SelectedAudioTrack = null;
        if (SelectedFilePath is null) return;
        try
        {
            var ffprobe = settings.FfprobePath ?? "ffprobe";
            var media = await new FfprobeMediaProbe(ffprobe).ProbeAsync(SelectedFilePath, CancellationToken.None);
            foreach (var track in media.AudioTracks)
                AudioTracks.Add(new AudioTrackOption(track.StreamIndex,
                    $"音轨 {track.StreamIndex} · {track.Language ?? "未知语言"} · {track.Title ?? track.Codec}" +
                    (track.IsDefault ? " · 默认" : string.Empty)));
            SelectedAudioTrack = AudioTracks.FirstOrDefault(x =>
                media.AudioTracks.First(t => t.StreamIndex == x.StreamIndex).IsDefault) ?? AudioTracks.FirstOrDefault();
            Notify(nameof(AudioTracks));
            StatusMessage = AudioTracks.Count == 0 ? "视频中没有检测到音轨。" : $"检测到 {AudioTracks.Count} 条音轨。";
        }
        catch (Exception exception) { StatusMessage = $"暂时无法读取音轨：{exception.Message}"; }
    }

    public void Cancel()
    {
        runCancellation?.Cancel(); modelDownloadCancellation?.Cancel();
        componentInstallCancellation?.Cancel(); selfTestCancellation?.Cancel();
    }

    public async Task SelectLocalModelAsync(string path)
    {
        if (!TryValidateModel(path, out var error)) { ModelStatus = error; return; }
        selectedModelPath = Path.GetFullPath(path);
        settings = settings with { WhisperModelPath = selectedModelPath };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
        ModelStatus = DescribeModel(selectedModelPath);
        ValidationMessage = string.Empty;
        RefreshCommands();
        await RefreshEnvironmentAsync();
    }

    public async Task SelectFfmpegAsync(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var ffprobe = Path.Combine(directory, "ffprobe.exe");
        settings = settings with { FfmpegPath = Path.GetFullPath(path), FfprobePath = File.Exists(ffprobe) ? ffprobe : settings.FfprobePath };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
        await RefreshEnvironmentAsync();
    }

    public async Task SelectVadAsync(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || !file.Name.Contains("silero", StringComparison.OrdinalIgnoreCase) || file.Length > 50L * 1024 * 1024)
        {
            EnvironmentStatus = "所选文件不是有效的 Silero VAD；请使用 ggml-silero-*.bin，而不是 Whisper 语音模型。";
            return;
        }
        settings = settings with { VadModelPath = Path.GetFullPath(path) };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
        await RefreshEnvironmentAsync();
    }

    public async Task SelectRuntimeAsync(string directory)
    {
        if (!File.Exists(Path.Combine(directory, "whisper.dll")))
        { EnvironmentStatus = "所选目录中没有 whisper.dll。"; return; }
        settings = settings with { WhisperRuntimePath = Path.GetFullPath(directory) };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
        await RefreshEnvironmentAsync();
    }

    public async Task SelectCustomOutputDirectoryAsync(string directory)
    {
        CustomOutputDirectory = Path.GetFullPath(directory);
        SelectedPublishLocation = "自定义目录";
        await SavePublicationSettingsAsync();
    }

    public async Task RemoveManagedComponentsAsync()
    {
        if (IsRunning || IsComponentInstalling) return;
        var root = Path.GetFullPath(Path.Combine(GetUserDataRoot(), "components"));
        foreach (var name in new[] { "vad", "runtime-cpu-1.9.1", "runtime-cuda-1.9.1" })
        {
            var path = Path.GetFullPath(Path.Combine(root, name));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("组件目录越界。");
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        if (IsUnderManagedComponents(settings.VadModelPath, root)) settings = settings with { VadModelPath = null };
        if (IsUnderManagedComponents(settings.WhisperRuntimePath, root)) settings = settings with { WhisperRuntimePath = null };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
        ComponentInstallStatus = "应用管理的 VAD 和 Whisper runtime 已移除。";
        await RefreshEnvironmentAsync();
    }

    public async Task SaveApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) { ApiKeyStatus = "请输入有效的 API Key。"; return; }
        await secretStore.WriteAsync("deepseek-api-key", apiKey, CancellationToken.None);
        deepSeekApiKey = apiKey.Trim();
        ApiKeyStatus = "DeepSeek API Key 已加密保存（当前 Windows 用户）。";
        DeepSeekConnectionStatus = "密钥已更新，请执行连接测试。";
        Notify(nameof(HasSavedApiKey));
        Notify(nameof(NeedsInitialSetup));
        RefreshCommands();
    }

    private bool CanStart() => SelectedFilePath is not null && !IsRunning;

    public async Task PrepareBatchAsync()
    {
        await RefreshEnvironmentAsync();
        if (environmentReport?.CanGenerateSubtitles != true || selectedModelPath is null || settings.VadModelPath is null)
            throw new InvalidOperationException("运行组件尚未配置完整，请先完成配置向导。");
        if (string.IsNullOrWhiteSpace(deepSeekApiKey))
            throw new InvalidOperationException("请先在配置向导中保存 DeepSeek API Key。");
        await SavePublicationSettingsAsync();
    }

    public async Task<BatchExecutionResult> RunBatchItemAsync(
        string mediaPath, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath)) throw new FileNotFoundException("视频文件不存在。", mediaPath);
        if (!SupportedExtensions.Contains(Path.GetExtension(mediaPath))) throw new InvalidOperationException("暂不支持此视频格式。");
        var modelPath = selectedModelPath ?? throw new InvalidOperationException("尚未配置 Whisper 模型。");
        var vadPath = settings.VadModelPath ?? throw new InvalidOperationException("尚未配置 VAD 模型。");
        var projectName = MakeSafeName(Path.GetFileNameWithoutExtension(mediaPath)) + "-" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(mediaPath).ToUpperInvariant())))[..8].ToLowerInvariant();
        var projectDirectory = Path.Combine(GetUserDataRoot(), "projects", projectName);
        var outputDirectory = Path.Combine(projectDirectory, "exports");
        var qualityMode = SelectedQualityMode.StartsWith("生成", StringComparison.Ordinal)
            ? SubtitleQualityMode.Suggest
            : SelectedQualityMode == "关闭" ? SubtitleQualityMode.Off : SubtitleQualityMode.Auto;
        var request = new SubtitleGenerationRequest(
            mediaPath, modelPath, vadPath, projectDirectory, outputDirectory,
            settings.DeepSeekModel, qualityMode, TranslationQaEnabled,
            SourceLanguageCode(SelectedSourceLanguage), deepSeekApiKey,
            settings.FfmpegPath, settings.FfprobePath, settings.WhisperRuntimePath);
        var result = await generationService.GenerateAsync(request, progress, cancellationToken);
        var preferred = SelectedOutputMode == "仅中文字幕" ? result.ChineseSubtitle : result.BilingualSubtitle;
        var publication = await publicationService.PublishAndRecordAsync(new SubtitlePublicationRequest(
            mediaPath, preferred, projectDirectory, BuildPublicationOptions()), cancellationToken);
        return new BatchExecutionResult(publication.Success ? publication.PublishedPath ?? preferred : preferred, publication.Message);
    }

    private async Task StartAsync()
    {
        if (SelectedFilePath is null) return;
        var modelPath = selectedModelPath;
        await RefreshEnvironmentAsync();
        var vadPath = settings.VadModelPath;
        if (environmentReport?.CanGenerateSubtitles != true || modelPath is null || vadPath is null)
        {
            ValidationMessage = "运行组件尚未配置完整，请根据“运行组件”检测结果完成设置。";
            return;
        }
        if (string.IsNullOrWhiteSpace(deepSeekApiKey))
        {
            ValidationMessage = "请先在翻译服务区域配置 DeepSeek API Key。";
            return;
        }

        var projectName = MakeSafeName(Path.GetFileNameWithoutExtension(SelectedFilePath));
        var projectDirectory = Path.Combine(GetUserDataRoot(), "projects", projectName);
        var outputDirectory = Path.Combine(projectDirectory, "exports");
        var qualityMode = SelectedQualityMode.StartsWith("生成", StringComparison.Ordinal)
            ? SubtitleQualityMode.Suggest
            : SelectedQualityMode == "关闭" ? SubtitleQualityMode.Off : SubtitleQualityMode.Auto;
        var request = new SubtitleGenerationRequest(
            SelectedFilePath, modelPath, vadPath, projectDirectory, outputDirectory,
            "deepseek-v4-flash", qualityMode, TranslationQaEnabled,
            DeepSeekApiKey: deepSeekApiKey,
            FfmpegPath: settings.FfmpegPath,
            FfprobePath: settings.FfprobePath,
            NativeRuntimePath: settings.WhisperRuntimePath);
        request = request with
        {
            SourceLanguage = SourceLanguageCode(SelectedSourceLanguage),
            AudioStreamIndex = SelectedAudioTrack?.StreamIndex
        };

        runCancellation = new CancellationTokenSource();
        IsRunning = true;
        ResultSubtitlePath = null;
        resultOutputDirectory = null;
        resultProjectDirectory = null;
        OverallProgress = 0;
        CurrentStage = "准备任务";
        ValidationMessage = string.Empty;
        StatusMessage = "正在启动字幕任务……";
        var progress = new Progress<PipelineProgress>(p =>
        {
            CurrentStage = StageName(p.Stage);
            OverallProgress = CalculateOverallProgress(p.Stage, p.Percent);
            StatusMessage = p.Message ?? "正在处理……";
        });
        try
        {
            await SavePublicationSettingsAsync();
            var result = await generationService.GenerateAsync(request, progress, runCancellation.Token);
            var preferred = SelectedOutputMode == "仅中文字幕" ? result.ChineseSubtitle : result.BilingualSubtitle;
            var publicationRequest = new SubtitlePublicationRequest(
                SelectedFilePath, preferred, projectDirectory, BuildPublicationOptions());
            var publication = await publicationService.PublishAndRecordAsync(publicationRequest, runCancellation.Token);
            resultProjectDirectory = projectDirectory;
            ResultSubtitlePath = publication.Success ? publication.PublishedPath ?? preferred : preferred;
            resultOutputDirectory = Path.GetDirectoryName(ResultSubtitlePath);
            CurrentStage = "处理完成";
            OverallProgress = 100;
            StatusMessage = publication.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "任务已取消；当前阶段已记录为已取消，缓存可在下次运行时继续复用。";
            CurrentStage = "已取消";
        }
        catch (Exception exception)
        {
            ValidationMessage = exception.Message;
            StatusMessage = "任务失败。可修正问题后重新开始，已完成阶段不会重复处理。";
            CurrentStage = "处理失败";
        }
        finally
        {
            runCancellation.Dispose();
            runCancellation = null;
            IsRunning = false;
        }
    }

    private void RefreshCommands()
    {
        (StartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenSubtitleCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (OpenOutputFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DownloadModelCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelModelDownloadCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (DeleteApiKeyCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (TestDeepSeekConnectionCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (RefreshEnvironmentCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (InstallVadCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (InstallCpuRuntimeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (InstallCudaRuntimeCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (CancelComponentInstallCommand as RelayCommand)?.RaiseCanExecuteChanged();
        (RunSelfTestCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private static string StageName(string stage) => stage switch
    {
        "probe" => "读取媒体", "audio" => "提取音频", "transcription" => "语音识别",
        "vad" => "检测语音", "transcribe" => "语音识别",
        "translation" => "翻译", "translation-qa" => "翻译 QA", "final-qc" => "最终质检",
        "export" => "导出", "error" => "错误", _ => "处理中"
    };

    private SubtitlePublicationOptions BuildPublicationOptions() => new(
        SelectedPublishLocation == "自定义目录" ? SubtitlePublishLocation.CustomDirectory :
            SelectedPublishLocation == "仅项目目录" ? SubtitlePublishLocation.ProjectOnly : SubtitlePublishLocation.VideoDirectory,
        SelectedNamingStrategy == "与视频完全同名" ? SubtitleNamingStrategy.SameAsVideo :
            SelectedNamingStrategy == "自定义模板" ? SubtitleNamingStrategy.CustomTemplate : SubtitleNamingStrategy.VideoNameWithTags,
        SelectedConflictPolicy == "自动编号" ? SubtitleConflictPolicy.AutoNumber : SubtitleConflictPolicy.BackupAndOverwrite,
        string.IsNullOrWhiteSpace(CustomOutputDirectory) ? null : CustomOutputDirectory,
        NamingTemplate,
        "zh-CN",
        SelectedOutputMode == "仅中文字幕" ? "chinese" : "bilingual");

    public async Task SavePublicationSettingsAsync()
    {
        var options = BuildPublicationOptions();
        settings = settings with
        {
            SubtitlePublishLocation = options.Location,
            SubtitleNamingStrategy = options.NamingStrategy,
            SubtitleConflictPolicy = options.ConflictPolicy,
            SubtitleCustomDirectory = options.CustomDirectory,
            SubtitleNamingTemplate = options.NamingTemplate
        };
        await settingsStore.SaveAsync(settings, CancellationToken.None);
    }

    private string BuildPublicationPreview()
    {
        if (SelectedFilePath is null) return "选择视频后将在这里预览最终字幕路径。";
        var projectName = MakeSafeName(Path.GetFileNameWithoutExtension(SelectedFilePath));
        var projectDirectory = Path.Combine(GetUserDataRoot(), "projects", projectName);
        try
        {
            return publicationService.BuildTargetPath(new SubtitlePublicationRequest(
                SelectedFilePath, Path.Combine(projectDirectory, "exports", "preview.srt"), projectDirectory,
                BuildPublicationOptions()));
        }
        catch (Exception exception) { return "命名设置有误：" + exception.Message; }
    }

    private static string PublishLocationName(SubtitlePublishLocation value) => value switch
    { SubtitlePublishLocation.CustomDirectory => "自定义目录", SubtitlePublishLocation.ProjectOnly => "仅项目目录", _ => "视频所在目录（推荐）" };
    private static string NamingStrategyName(SubtitleNamingStrategy value) => value switch
    { SubtitleNamingStrategy.SameAsVideo => "与视频完全同名", SubtitleNamingStrategy.CustomTemplate => "自定义模板", _ => "视频名 + 语言和类型（推荐）" };
    private static string ConflictPolicyName(SubtitleConflictPolicy value) => value == SubtitleConflictPolicy.AutoNumber
        ? "自动编号" : "覆盖前备份（推荐）";

    private static string SourceLanguageCode(string language) => language switch
    {
        "英语" => "en", "日语" => "ja", "韩语" => "ko", "法语" => "fr",
        "德语" => "de", "西班牙语" => "es", "俄语" => "ru", _ => "auto"
    };

    private static double CalculateOverallProgress(string stage, double? stagePercent)
    {
        var local = Math.Clamp(stagePercent ?? 0, 0, 100) / 100d;
        var (start, weight) = stage switch
        {
            "probe" => (0d, 2d), "audio" => (2d, 8d), "vad" => (10d, 8d),
            "transcription" or "transcribe" => (18d, 47d), "translation" => (65d, 20d),
            "translation-qa" => (85d, 8d), "final-qc" => (93d, 4d), "export" => (97d, 3d),
            _ => (0d, 0d)
        };
        return Math.Min(100, start + weight * local);
    }

    private void OpenSubtitle()
    {
        if (!HasResult) return;
        new SubtitleEditorWindow(ResultSubtitlePath!, SelectedFilePath!, resultProjectDirectory)
        {
            Owner = System.Windows.Application.Current.MainWindow
        }.ShowDialog();
    }

    private void OpenOutputFolder()
    {
        if (resultOutputDirectory is null || !Directory.Exists(resultOutputDirectory)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", resultOutputDirectory) { UseShellExecute = true });
    }

    private static string FindWorkspaceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "SubtitleTranslator.slnx"))) return directory.FullName;
        return Directory.GetCurrentDirectory();
    }

    private async Task DownloadSelectedModelAsync()
    {
        var model = SelectedSpeechModel.StartsWith("Small", StringComparison.Ordinal)
            ? ModelCatalog.Small : ModelCatalog.Turbo;
        modelDownloadCancellation = new CancellationTokenSource();
        IsModelDownloading = true;
        ModelDownloadProgress = 0;
        var progress = new Progress<ModelDownloadProgress>(p =>
        { ModelDownloadProgress = p.Percent; ModelStatus = p.Message; });
        try
        {
            var path = await modelDownloadService.DownloadAsync(
                model, Path.Combine(GetUserDataRoot(), "models"), progress, modelDownloadCancellation.Token);
            await SelectLocalModelAsync(path);
            ModelDownloadProgress = 100;
        }
        catch (OperationCanceledException) { ModelStatus = "模型下载已暂停，下次可从断点继续。"; }
        catch (Exception exception) { ModelStatus = $"模型下载失败：{exception.Message}"; }
        finally
        {
            modelDownloadCancellation.Dispose(); modelDownloadCancellation = null; IsModelDownloading = false;
        }
    }

    private async Task DeleteApiKeyAsync()
    {
        await secretStore.DeleteAsync("deepseek-api-key", CancellationToken.None);
        deepSeekApiKey = null;
        ApiKeyStatus = "DeepSeek API Key 已删除。";
        DeepSeekConnectionStatus = "保存密钥后可测试连接。";
        Notify(nameof(HasSavedApiKey));
        Notify(nameof(NeedsInitialSetup));
        RefreshCommands();
    }

    private async Task TestDeepSeekConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(deepSeekApiKey)) return;
        IsTestingDeepSeek = true;
        DeepSeekConnectionStatus = "正在连接 DeepSeek……";
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deepSeekApiKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            DeepSeekConnectionStatus = response.StatusCode switch
            {
                System.Net.HttpStatusCode.OK => "✓ DeepSeek 连接成功，API Key 有效。",
                System.Net.HttpStatusCode.Unauthorized => "连接失败：API Key 无效或已失效（HTTP 401）。",
                System.Net.HttpStatusCode.PaymentRequired => "API Key 有效，但账户余额不足（HTTP 402）。",
                System.Net.HttpStatusCode.TooManyRequests => "请求过于频繁（HTTP 429），请稍后重试。",
                >= System.Net.HttpStatusCode.InternalServerError => $"DeepSeek 服务暂时不可用（HTTP {(int)response.StatusCode}），请稍后重试。",
                _ => $"DeepSeek 返回 HTTP {(int)response.StatusCode}，请检查账户和网络设置。"
            };
        }
        catch (TaskCanceledException) { DeepSeekConnectionStatus = "连接超时，请检查网络或代理设置。"; }
        catch (HttpRequestException exception) { DeepSeekConnectionStatus = $"无法连接 DeepSeek：{exception.Message}"; }
        finally { IsTestingDeepSeek = false; }
    }

    public async Task RefreshEnvironmentAsync()
    {
        environmentReport = await environmentDiagnosticService.DiagnoseAsync(settings, CancellationToken.None);
        var hardware = await hardwareDiagnosticService.DiagnoseAsync(settings, CancellationToken.None);
        settings = settings with
        {
            FfmpegPath = environmentReport.Components.First(x => x.Id == "ffmpeg").ResolvedPath,
            FfprobePath = environmentReport.Components.First(x => x.Id == "ffprobe").ResolvedPath
        };
        EnvironmentStatus = string.Join("  ·  ", environmentReport.Components.Select(x =>
            x.State == ComponentState.Ready ? $"✓ {x.DisplayName}" : $"需处理 {x.DisplayName}：{x.Message}"));
        var gpu = hardware.HasNvidiaGpu
            ? $"GPU：{hardware.GpuName}，驱动 {hardware.DriverVersion}，计算能力 {hardware.ComputeCapability}"
            : "未检测到 NVIDIA GPU";
        var cuda = hardware.HasCudaToolkit ? $"CUDA：{hardware.CudaToolkitVersion}" : "CUDA Toolkit：未检测到";
        HardwareStatus = $"{gpu}  ·  {cuda}  ·  当前 runtime：{hardware.RuntimeKind}" +
            (hardware.Warnings.Count == 0 ? string.Empty : $"  ·  {string.Join("；", hardware.Warnings)}");
        Notify(nameof(NeedsInitialSetup));
    }

    private bool CanInstallComponent() => !IsRunning && !IsModelDownloading && !IsComponentInstalling;

    private async Task InstallComponentAsync(DownloadableComponent component)
    {
        componentInstallCancellation = new CancellationTokenSource();
        IsComponentInstalling = true;
        ComponentInstallProgress = 0;
        var progress = new Progress<ModelDownloadProgress>(p =>
        { ComponentInstallProgress = p.Percent; ComponentInstallStatus = p.Message; });
        try
        {
            var result = await componentInstallService.InstallAsync(
                component, Path.Combine(GetUserDataRoot(), "components"), progress, componentInstallCancellation.Token);
            settings = component.Id switch
            {
                "silero-vad" => settings with { VadModelPath = result.RequiredPath },
                "whisper-runtime-cpu" or "whisper-runtime-cuda" => settings with { WhisperRuntimePath = result.InstallDirectory },
                _ => settings
            };
            await settingsStore.SaveAsync(settings, CancellationToken.None);
            ComponentInstallProgress = 100;
            ComponentInstallStatus = $"{component.DisplayName} 安装完成。";
            await RefreshEnvironmentAsync();
        }
        catch (OperationCanceledException)
        { ComponentInstallStatus = "组件下载已暂停，下次会从断点继续。"; }
        catch (Exception exception)
        { ComponentInstallStatus = $"组件安装失败：{exception.Message}"; }
        finally
        {
            componentInstallCancellation.Dispose(); componentInstallCancellation = null; IsComponentInstalling = false;
        }
    }

    private static void OpenFfmpegDownloadPage() => Process.Start(new ProcessStartInfo(
        "https://ffmpeg.org/download.html#build-windows") { UseShellExecute = true });

    private bool CanRunSelfTest() => !IsRunning && !IsComponentInstalling && !IsSelfTesting &&
        selectedModelPath is not null && settings.WhisperRuntimePath is not null;

    private async Task RunSelfTestAsync()
    {
        if (selectedModelPath is null || settings.WhisperRuntimePath is null) return;
        selfTestCancellation = new CancellationTokenSource();
        IsSelfTesting = true;
        SelfTestStatus = "正在启动本地推理自检……";
        var progress = new Progress<PipelineProgress>(p => SelfTestStatus = p.Message ?? "正在自检……");
        try
        {
            var result = await selfTestService.RunAsync(
                selectedModelPath, settings.WhisperRuntimePath, progress, selfTestCancellation.Token);
            SelfTestStatus = $"自检通过：{result.Message}，耗时 {result.Elapsed.TotalSeconds:0.0} 秒。";
        }
        catch (OperationCanceledException) { SelfTestStatus = "推理自检已取消。"; }
        catch (Exception exception) { SelfTestStatus = $"自检失败：{exception.Message}"; }
        finally
        {
            selfTestCancellation.Dispose(); selfTestCancellation = null; IsSelfTesting = false;
        }
    }

    private UserSettings ApplyDevelopmentFallbacks(UserSettings current, string? modelPath)
    {
        var vad = current.VadModelPath;
        var developmentVad = Path.Combine(workspaceRoot, "models", "ggml-silero-v6.2.0.bin");
        if (vad is null && File.Exists(developmentVad)) vad = developmentVad;
        var runtime = current.WhisperRuntimePath;
        var developmentRuntime = Path.Combine(AppContext.BaseDirectory, "runtimes", "cuda", "win-x64");
        if (runtime is null && File.Exists(Path.Combine(developmentRuntime, "whisper.dll"))) runtime = developmentRuntime;
        return current with { WhisperModelPath = modelPath, VadModelPath = vad, WhisperRuntimePath = runtime };
    }

    private string? ResolveInitialModel(string? configured)
    {
        if (configured is not null && File.Exists(configured)) return Path.GetFullPath(configured);
        var developmentDefault = Path.Combine(workspaceRoot, "models", "ggml-large-v3-turbo-q5_0.bin");
        return File.Exists(developmentDefault) ? developmentDefault : null;
    }

    private static bool TryValidateModel(string path, out string error)
    {
        if (!File.Exists(path)) { error = "模型文件不存在。"; return false; }
        if (!Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase))
        { error = "请选择 .bin 格式的 Whisper GGML 模型。"; return false; }
        if (new FileInfo(path).Length < 10 * 1024 * 1024)
        { error = "模型文件过小，可能不是有效的 Whisper GGML 模型。"; return false; }
        error = string.Empty; return true;
    }

    private static string DescribeModel(string? path) => path is null
        ? "尚未选择模型，可选择本地 .bin 文件或下载推荐模型。"
        : $"当前模型：{Path.GetFileName(path)}（{new FileInfo(path).Length / 1024d / 1024:0} MB）\n{path}";

    private static string GetUserDataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AI字幕翻译");

    private static bool IsUnderManagedComponents(string? path, string root) =>
        path is not null && Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string MakeSafeName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(value.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(clean) ? "subtitle-project" : clean;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (!EqualityComparer<T>.Default.Equals(field, value)) { field = value; Notify(name); } }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record AudioTrackOption(int StreamIndex, string DisplayName);

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !executing && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        executing = true; RaiseCanExecuteChanged();
        try { await execute(); }
        finally { executing = false; RaiseCanExecuteChanged(); }
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
