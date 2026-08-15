using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;
using SubtitleTranslator.Media;
using SubtitleTranslator.Speech;
using SubtitleTranslator.Subtitles;
using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Orchestration;

public sealed class InProcessSubtitleGenerationService(string workspaceRoot) : ISubtitleGenerationService
{
    public async Task<SubtitleGenerationResult> GenerateAsync(
        SubtitleGenerationRequest request,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ProjectRunTracker? tracker = null;
        string? activeStage = null, activeKey = null;
        try
        {
            Validate(request);
            Directory.CreateDirectory(request.ProjectDirectory);
            Directory.CreateDirectory(request.OutputDirectory);
            var stageCache = new FileStageCache(Path.Combine(request.ProjectDirectory, "cache", "stages"));
            var fingerprint = await new SourceFileFingerprintService().ComputeAsync(request.MediaPath, cancellationToken);
            tracker = await ProjectRunTracker.OpenAsync(
                request.ProjectDirectory, Path.GetFileNameWithoutExtension(request.MediaPath), fingerprint, cancellationToken);

            var ffmpeg = request.FfmpegPath ?? FindTool("ffmpeg.exe", "ffmpeg");
            var ffprobe = request.FfprobePath ?? FindTool("ffprobe.exe", "ffprobe");
            var probeKey = PipelineCacheKeyBuilder.Build("probe", 1, new { executable = ffprobe }, fingerprint.Sha256);
            (activeStage, activeKey) = await BeginAsync(tracker, "probe", probeKey, progress, cancellationToken);
            var media = await new FfprobeMediaProbe(ffprobe).ProbeAsync(request.MediaPath, cancellationToken);
            await tracker.CompleteAsync("probe", probeKey, [], cancellationToken);
            (activeStage, activeKey) = (null, null);
            if (media.AudioTracks.Count == 0) throw new InvalidOperationException("视频中没有可用音轨。");

            var streamIndex = media.AudioTracks.FirstOrDefault(x => x.IsDefault)?.StreamIndex ?? media.AudioTracks[0].StreamIndex;
            var audioKey = PipelineCacheKeyBuilder.Build(
                "audio", 1, new { streamIndex, sampleRate = 16000, channels = 1, codec = "pcm_s16le" }, fingerprint.Sha256);
            (activeStage, activeKey) = await BeginAsync(tracker, "audio", audioKey, progress, cancellationToken);
            var audioPath = Path.Combine(request.ProjectDirectory, "cache", "media", "audio.wav");
            IAudioExtractor extractor = new CachingAudioExtractor(
                new FfmpegAudioExtractor(ffmpeg), stageCache, audioKey);
            var audio = await extractor.ExtractAsync(media.Path, streamIndex, audioPath, progress, cancellationToken);
            audio = audio with { Duration = media.Duration };
            await tracker.CompleteAsync("audio", audioKey, [audio.Path], cancellationToken);
            (activeStage, activeKey) = (null, null);

            var modelInfo = new FileInfo(request.ModelPath);
            var vadInfo = new FileInfo(request.VadModelPath);
            var transcriptionKey = PipelineCacheKeyBuilder.Build("transcription", 2, new
            {
                model = new { modelInfo.Name, modelInfo.Length, modelInfo.LastWriteTimeUtc },
                request.SourceLanguage,
                threads = 4,
                noContext = false,
                vad = new { vadInfo.Name, vadInfo.Length, vadInfo.LastWriteTimeUtc, threshold = 0.5, maximumGapSeconds = 20 },
                retry = true
            }, audioKey);
            (activeStage, activeKey) = await BeginAsync(tracker, "transcription", transcriptionKey, progress, cancellationToken);
            var vadOptions = new VoiceActivityOptions(
                request.VadModelPath, 0.5f, TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(200));
            ITranscriptionEngine engine = new VadWhisperNetTranscriptionEngine(
                new WhisperNetVoiceActivityDetector(), new FfmpegAudioRegionExtractor(ffmpeg), vadOptions,
                TimeSpan.FromSeconds(20), TimeSpan.FromMinutes(5),
                Path.Combine(request.ProjectDirectory, "cache", "speech-windows"));
            engine = new RetryingTranscriptionEngine(
                engine, new WhisperNetTranscriptionEngine(), new FfmpegAudioRegionExtractor(ffmpeg),
                Path.Combine(request.ProjectDirectory, "cache", "retry-windows"),
                TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(10));
            engine = new CachingTranscriptionEngine(engine, stageCache, transcriptionKey);
            var transcription = await engine.TranscribeAsync(
                audio, new TranscriptionOptions(
                    request.ModelPath, request.SourceLanguage, Threads: 4,
                    NativeRuntimePath: request.NativeRuntimePath), progress, cancellationToken);
            var transcriptJson = Path.Combine(request.OutputDirectory, "transcript.json");
            var transcriptSrt = Path.Combine(request.OutputDirectory, "transcript.srt");
            await WriteJsonAsync(transcriptJson, transcription, cancellationToken);
            await new SrtExporter().ExportAsync(transcription.Segments, transcriptSrt, cancellationToken);
            await tracker.CompleteAsync("transcription", transcriptionKey, [transcriptJson, transcriptSrt], cancellationToken);
            (activeStage, activeKey) = (null, null);

            var apiKey = request.DeepSeekApiKey ?? LocalApiKeyResolver.ReadDeepSeekApiKey(workspaceRoot);
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("未找到 DEEPSEEK_API_KEY，请检查 .env 文件。");
            var translationKey = PipelineCacheKeyBuilder.Build("translation", 2, new
            {
                provider = "deepseek", model = request.DeepSeekModel, prompt = "deepseek-json-v2-context",
                sourceLanguage = request.SourceLanguage, targetLanguage = "zh-CN"
            }, transcriptionKey);
            (activeStage, activeKey) = await BeginAsync(tracker, "translation", translationKey, progress, cancellationToken);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            ITranslationProvider provider = new DeepSeekTranslationProvider(
                client, new DeepSeekTranslationOptions(apiKey, request.DeepSeekModel));
            var translationCache = Path.Combine(request.ProjectDirectory, "cache", "translation");
            provider = new CachingTranslationProvider(provider, translationCache, $"deepseek-json-v2-context:{request.DeepSeekModel}");
            IReadOnlyList<TranslationSegment> translations = await new TranslationOrchestrator(provider).TranslateAsync(
                transcription.Segments, request.SourceLanguage,
                new TranslationContext(Title: Path.GetFileNameWithoutExtension(media.Path)),
                new TranslationOptions(), progress, cancellationToken);
            await tracker.CompleteAsync("translation", translationKey, [translationCache], cancellationToken);
            (activeStage, activeKey) = (null, null);

            var upstreamKey = translationKey;
            if (request.TranslationQa)
            {
                var qaKey = PipelineCacheKeyBuilder.Build(
                    "translation-qa", 1, new { analyzer = "ambiguity-v1", provider = request.DeepSeekModel }, translationKey);
                (activeStage, activeKey) = await BeginAsync(tracker, "translation-qa", qaKey, progress, cancellationToken);
                translations = await new TranslationReviewOrchestrator(new DeepSeekTranslationReviewProvider(provider)).ReviewAsync(
                    transcription.Segments, translations,
                    new TranslationContext(Title: Path.GetFileNameWithoutExtension(media.Path)), progress, cancellationToken);
                await tracker.CompleteAsync("translation-qa", qaKey, [translationCache], cancellationToken);
                (activeStage, activeKey) = (null, null);
                upstreamKey = qaKey;
            }

            var qcKey = PipelineCacheKeyBuilder.Build("final-qc", 1,
                new { mode = request.QualityMode.ToString(), rules = "text-v2-conservative-reading-v1" }, upstreamKey);
            (activeStage, activeKey) = await BeginAsync(tracker, "final-qc", qcKey, progress, cancellationToken);
            var qc = new FinalSubtitleQualityProcessor().Process(transcription.Segments, translations, request.QualityMode);
            var qcReport = Path.Combine(request.OutputDirectory, "qc-report.json");
            await WriteJsonAsync(qcReport, qc.Report, cancellationToken);
            await tracker.CompleteAsync("final-qc", qcKey, [qcReport], cancellationToken);
            (activeStage, activeKey) = (null, null);

            var exportKey = PipelineCacheKeyBuilder.Build("export", 1,
                new { original = true, chinese = true, bilingual = true }, qcKey);
            (activeStage, activeKey) = await BeginAsync(tracker, "export", exportKey, progress, cancellationToken);
            var chinese = Path.Combine(request.OutputDirectory, "chinese.srt");
            var bilingual = Path.Combine(request.OutputDirectory, "bilingual.srt");
            var exporter = new TranslatedSrtExporter();
            await exporter.ExportAsync(transcription.Segments, qc.Translations, TranslatedSubtitleLayout.ChineseOnly, chinese, cancellationToken);
            await exporter.ExportAsync(transcription.Segments, qc.Translations, TranslatedSubtitleLayout.OriginalThenChinese, bilingual, cancellationToken);
            await tracker.CompleteAsync("export", exportKey, [transcriptSrt, chinese, bilingual], cancellationToken);
            progress?.Report(new PipelineProgress("export", 100, "字幕生成完成。"));
            return new SubtitleGenerationResult(
                Path.Combine(request.ProjectDirectory, "project.json"), transcriptSrt, chinese, bilingual, qcReport);
        }
        catch (OperationCanceledException)
        {
            if (tracker is not null && activeStage is not null && activeKey is not null)
                await tracker.CancelAsync(activeStage, activeKey, CancellationToken.None);
            throw;
        }
        catch (Exception exception)
        {
            if (tracker is not null && activeStage is not null && activeKey is not null)
                await tracker.FailAsync(activeStage, activeKey, exception, CancellationToken.None);
            throw;
        }
    }

    private async Task<(string Stage, string Key)> BeginAsync(
        ProjectRunTracker tracker, string stage, string key, IProgress<PipelineProgress>? progress, CancellationToken token)
    {
        progress?.Report(new PipelineProgress(stage, 0, "开始处理……"));
        await tracker.BeginAsync(stage, key, token);
        return (stage, key);
    }

    private string FindTool(string localName, string fallback)
    {
        var bundled = Path.Combine(AppContext.BaseDirectory, "tools", localName);
        if (File.Exists(bundled)) return bundled;
        var local = Path.Combine(workspaceRoot, "models", localName);
        return File.Exists(local) ? local : fallback;
    }

    private static void Validate(SubtitleGenerationRequest request)
    {
        if (!File.Exists(request.MediaPath)) throw new FileNotFoundException("视频文件不存在。", request.MediaPath);
        if (!File.Exists(request.ModelPath)) throw new FileNotFoundException("Whisper 模型不存在。", request.ModelPath);
        if (!File.Exists(request.VadModelPath)) throw new FileNotFoundException("VAD 模型不存在。", request.VadModelPath);
        if (request.FfmpegPath is not null && !File.Exists(request.FfmpegPath)) throw new FileNotFoundException("FFmpeg 不存在。", request.FfmpegPath);
        if (request.FfprobePath is not null && !File.Exists(request.FfprobePath)) throw new FileNotFoundException("FFprobe 不存在。", request.FfprobePath);
        if (request.NativeRuntimePath is not null && !File.Exists(Path.Combine(request.NativeRuntimePath, "whisper.dll")))
            throw new FileNotFoundException("Whisper runtime 目录中没有 whisper.dll。", request.NativeRuntimePath);
    }

    private static Task WriteJsonAsync<T>(string path, T value, CancellationToken token) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }), token);
}
