using System.Diagnostics;
using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Media;
using SubtitleTranslator.Speech;
using SubtitleTranslator.Subtitles;
using SubtitleTranslator.Translation;
using SubtitleTranslator.Infrastructure;

return await BenchmarkProgram.RunAsync(args);

internal static class BenchmarkProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (options.MediaPath is null || options.ModelPath is null)
        {
            Console.Error.WriteLine("Both --media and --model are required.");
            PrintHelp();
            return 2;
        }
        if (options.DeepSeekTranslation && string.IsNullOrWhiteSpace(ReadDeepSeekApiKey()))
        {
            Console.Error.WriteLine(
                "DEEPSEEK_API_KEY environment variable is required for --deepseek-translation.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        ProjectRunTracker? projectTracker = null;
        string? activeStage = null, activeStageKey = null;

        try
        {
            var outputDirectory = Path.GetFullPath(options.OutputDirectory ?? "benchmark-output");
            Directory.CreateDirectory(outputDirectory);
            var projectDirectory = options.ProjectDirectory is null
                ? null
                : Path.GetFullPath(options.ProjectDirectory);
            FileStageCache? projectCache = null;
            SourceFileFingerprint? sourceFingerprint = null;
            string? audioCacheKey = null;
            if (projectDirectory is not null)
            {
                Directory.CreateDirectory(projectDirectory);
                projectCache = new FileStageCache(Path.Combine(projectDirectory, "cache", "stages"));
                sourceFingerprint = await new SourceFileFingerprintService()
                    .ComputeAsync(options.MediaPath, cancellation.Token);
                projectTracker = await ProjectRunTracker.OpenAsync(
                    projectDirectory,
                    Path.GetFileNameWithoutExtension(options.MediaPath),
                    sourceFingerprint,
                    cancellation.Token);
            }
            var progress = new InlineProgress<PipelineProgress>(p =>
                Console.WriteLine($"[{p.Stage}] {(p.Percent is null ? "" : $"{p.Percent:0.0}% ")}{p.Message}"));

            var probeKey = sourceFingerprint is null ? null : PipelineCacheKeyBuilder.Build(
                "probe", 1, new { executable = options.FfprobePath }, sourceFingerprint.Sha256);
            if (projectTracker is not null && probeKey is not null)
            {
                activeStage = "probe"; activeStageKey = probeKey;
                await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
            }
            var probe = new FfprobeMediaProbe(options.FfprobePath);
            var media = await probe.ProbeAsync(options.MediaPath, cancellation.Token);
            if (projectTracker is not null && probeKey is not null)
            {
                await projectTracker.CompleteAsync("probe", probeKey, [], cancellation.Token);
                activeStage = activeStageKey = null;
            }
            PrintMediaInfo(media);

            if (media.AudioTracks.Count == 0)
                throw new InvalidOperationException("The media file has no audio tracks.");

            var streamIndex = options.StreamIndex ??
                media.AudioTracks.FirstOrDefault(track => track.IsDefault)?.StreamIndex ??
                media.AudioTracks[0].StreamIndex;

            if (media.AudioTracks.All(track => track.StreamIndex != streamIndex))
                throw new ArgumentException($"Audio stream {streamIndex} does not exist.");

            var wavPath = projectDirectory is null
                ? Path.Combine(outputDirectory, "audio.wav")
                : Path.Combine(projectDirectory, "cache", "media", "audio.wav");
            IAudioExtractor extractor = new FfmpegAudioExtractor(options.FfmpegPath);
            if (projectCache is not null && sourceFingerprint is not null)
            {
                audioCacheKey = PipelineCacheKeyBuilder.Build(
                    "audio", 1,
                    new { streamIndex, sampleRate = 16000, channels = 1, codec = "pcm_s16le" },
                    sourceFingerprint.Sha256);
                extractor = new CachingAudioExtractor(extractor, projectCache, audioCacheKey);
            }
            if (projectTracker is not null && audioCacheKey is not null)
            {
                activeStage = "audio"; activeStageKey = audioCacheKey;
                await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
            }
            var audio = await extractor.ExtractAsync(
                media.Path, streamIndex, wavPath, progress, cancellation.Token);
            audio = audio with { Duration = media.Duration };
            if (projectTracker is not null && audioCacheKey is not null)
            {
                await projectTracker.CompleteAsync("audio", audioCacheKey, [audio.Path], cancellation.Token);
                activeStage = activeStageKey = null;
            }

            var vadOptions = options.VadModelPath is null ? null : new VoiceActivityOptions(
                options.VadModelPath,
                options.VadThreshold,
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(500),
                TimeSpan.FromMilliseconds(200));

            ITranscriptionEngine transcriptionEngine = vadOptions is not null
                ? new VadWhisperNetTranscriptionEngine(
                    new WhisperNetVoiceActivityDetector(),
                    new FfmpegAudioRegionExtractor(options.FfmpegPath),
                    vadOptions,
                    TimeSpan.FromSeconds(options.VadMaximumGapSeconds),
                    TimeSpan.FromMinutes(options.ChunkMinutes ?? 5),
                    Path.Combine(outputDirectory, "speech-windows"))
                : options.ChunkMinutes is > 0
                    ? new ChunkedWhisperNetTranscriptionEngine(
                    new FfmpegAudioChunker(options.FfmpegPath),
                    TimeSpan.FromMinutes(options.ChunkMinutes.Value),
                    TimeSpan.FromSeconds(options.ChunkOverlapSeconds),
                    Path.Combine(outputDirectory, "chunks"))
                    : new WhisperNetTranscriptionEngine();
            if (vadOptions is not null && !options.NoRetry)
            {
                transcriptionEngine = new RetryingTranscriptionEngine(
                    transcriptionEngine,
                    new WhisperNetTranscriptionEngine(),
                    new FfmpegAudioRegionExtractor(options.FfmpegPath),
                    Path.Combine(outputDirectory, "retry-windows"),
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromSeconds(10));
            }
            string? transcriptionCacheKey = null;
            if (projectCache is not null && audioCacheKey is not null)
            {
                var modelInfo = new FileInfo(Path.GetFullPath(options.ModelPath));
                var vadModelInfo = options.VadModelPath is null
                    ? null
                    : new FileInfo(Path.GetFullPath(options.VadModelPath));
                transcriptionCacheKey = PipelineCacheKeyBuilder.Build(
                    "transcription", 2,
                    new
                    {
                        model = new { modelInfo.Name, modelInfo.Length, modelInfo.LastWriteTimeUtc },
                        options.Language,
                        options.Threads,
                        options.NoContext,
                        options.ChunkMinutes,
                        options.ChunkOverlapSeconds,
                        vad = vadModelInfo is null ? null : new
                        {
                            vadModelInfo.Name,
                            vadModelInfo.Length,
                            vadModelInfo.LastWriteTimeUtc,
                            options.VadThreshold,
                            options.VadMaximumGapSeconds
                        },
                        retry = !options.NoRetry
                    },
                    audioCacheKey);
                transcriptionEngine = new CachingTranscriptionEngine(
                    transcriptionEngine, projectCache, transcriptionCacheKey);
            }
            var wallClock = Stopwatch.StartNew();
            if (projectTracker is not null && transcriptionCacheKey is not null)
            {
                activeStage = "transcription"; activeStageKey = transcriptionCacheKey;
                await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
            }
            var result = await transcriptionEngine.TranscribeAsync(
                audio,
                new TranscriptionOptions(options.ModelPath, options.Language, Threads: options.Threads, NoContext: options.NoContext),
                progress,
                cancellation.Token);
            wallClock.Stop();

            var jsonPath = Path.Combine(outputDirectory, "transcript.json");
            await File.WriteAllTextAsync(jsonPath,
                JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }),
                cancellation.Token);

            var srtPath = Path.Combine(outputDirectory, "transcript.srt");
            await new SrtExporter().ExportAsync(result.Segments, srtPath, cancellation.Token);
            if (projectTracker is not null && transcriptionCacheKey is not null)
            {
                await projectTracker.CompleteAsync(
                    "transcription", transcriptionCacheKey, [jsonPath, srtPath], cancellation.Token);
                activeStage = activeStageKey = null;
            }

            string? chineseSrtPath = null, bilingualSrtPath = null, qcReportPath = null;
            string? translationStageKey = null, qaStageKey = null, qcStageKey = null;
            if (options.TestTranslation || options.DeepSeekTranslation)
            {
                HttpClient? deepSeekClient = null;
                ITranslationProvider translationProvider;
                if (options.DeepSeekTranslation)
                {
                    var apiKey = ReadDeepSeekApiKey();
                    if (string.IsNullOrWhiteSpace(apiKey))
                        throw new InvalidOperationException(
                            "DEEPSEEK_API_KEY environment variable is required for --deepseek-translation.");
                    deepSeekClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                    translationProvider = new DeepSeekTranslationProvider(
                        deepSeekClient,
                        new DeepSeekTranslationOptions(apiKey, options.DeepSeekModel));
                    translationProvider = new CachingTranslationProvider(
                        translationProvider,
                        options.TranslationCacheDirectory ?? (projectDirectory is null
                            ? Path.Combine(outputDirectory, "translation-cache")
                            : Path.Combine(projectDirectory, "cache", "translation")),
                        $"deepseek-json-v2-context:{options.DeepSeekModel}");
                }
                else
                {
                    Console.WriteLine("WARNING: --test-translation produces marked placeholder text, not real Chinese translation.");
                    translationProvider = new PrefixTestTranslationProvider();
                }

                if (projectCache is not null && transcriptionCacheKey is not null)
                    translationStageKey = PipelineCacheKeyBuilder.Build(
                        "translation", 2,
                        new
                        {
                            provider = options.DeepSeekTranslation ? "deepseek" : "test-prefix",
                            model = options.DeepSeekTranslation ? options.DeepSeekModel : "test",
                            prompt = options.DeepSeekTranslation ? "deepseek-json-v2-context" : "test-v1",
                            sourceLanguage = options.Language,
                            targetLanguage = "zh-CN"
                        },
                        transcriptionCacheKey);
                if (projectTracker is not null && translationStageKey is not null)
                {
                    activeStage = "translation"; activeStageKey = translationStageKey;
                    await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
                }
                IReadOnlyList<TranslationSegment> translations = await new TranslationOrchestrator(translationProvider)
                    .TranslateAsync(
                        result.Segments,
                        options.Language,
                        new TranslationContext(Title: Path.GetFileNameWithoutExtension(media.Path)),
                        new TranslationOptions(),
                        progress,
                        cancellation.Token);
                if (projectTracker is not null && translationStageKey is not null)
                {
                    await projectTracker.CompleteAsync(
                        "translation", translationStageKey,
                        [Path.Combine(projectDirectory!, "cache", "translation")], cancellation.Token);
                    activeStage = activeStageKey = null;
                }
                if (options.TranslationQa)
                {
                    if (!options.DeepSeekTranslation)
                        throw new InvalidOperationException("--translation-qa currently requires --deepseek-translation.");
                    if (translationStageKey is not null)
                        qaStageKey = PipelineCacheKeyBuilder.Build(
                            "translation-qa", 1,
                            new { analyzer = "ambiguity-v1", provider = options.DeepSeekModel },
                            translationStageKey);
                    if (projectTracker is not null && qaStageKey is not null)
                    {
                        activeStage = "translation-qa"; activeStageKey = qaStageKey;
                        await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
                    }
                    translations = await new TranslationReviewOrchestrator(
                            new DeepSeekTranslationReviewProvider(translationProvider))
                        .ReviewAsync(
                            result.Segments,
                            translations,
                            new TranslationContext(Title: Path.GetFileNameWithoutExtension(media.Path)),
                            progress,
                            cancellation.Token);
                    if (projectTracker is not null && qaStageKey is not null)
                    {
                        await projectTracker.CompleteAsync(
                            "translation-qa", qaStageKey,
                            [Path.Combine(projectDirectory!, "cache", "translation")], cancellation.Token);
                        activeStage = activeStageKey = null;
                    }
                }
                var qcUpstreamKey = qaStageKey ?? translationStageKey;
                if (qcUpstreamKey is not null)
                    qcStageKey = PipelineCacheKeyBuilder.Build(
                        "final-qc", 1,
                        new { mode = options.QualityMode.ToString(), rules = "text-v2-conservative-reading-v1" },
                        qcUpstreamKey);
                if (projectTracker is not null && qcStageKey is not null)
                {
                    activeStage = "final-qc"; activeStageKey = qcStageKey;
                    await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
                }
                var qcResult = new FinalSubtitleQualityProcessor().Process(
                    result.Segments, translations, options.QualityMode);
                translations = qcResult.Translations;
                qcReportPath = Path.Combine(outputDirectory, "qc-report.json");
                await File.WriteAllTextAsync(
                    qcReportPath,
                    JsonSerializer.Serialize(qcResult.Report, new JsonSerializerOptions { WriteIndented = true }),
                    cancellation.Token);
                if (projectTracker is not null && qcStageKey is not null)
                {
                    await projectTracker.CompleteAsync(
                        "final-qc", qcStageKey, [qcReportPath], cancellation.Token);
                    activeStage = activeStageKey = null;
                }
                Console.WriteLine(
                    $"[final-qc] mode={options.QualityMode}, issues={qcResult.Report.IssueCount}, " +
                    $"fixed={qcResult.Report.AppliedFixCount}, optional={qcResult.Report.OptionalConfirmationCount}");
                var suffix = options.DeepSeekTranslation ? "" : ".test";
                chineseSrtPath = Path.Combine(outputDirectory, $"chinese{suffix}.srt");
                bilingualSrtPath = Path.Combine(outputDirectory, $"bilingual{suffix}.srt");
                var exportUpstreamKey = qcStageKey ?? qaStageKey ?? translationStageKey;
                var exportStageKey = exportUpstreamKey is null ? null : PipelineCacheKeyBuilder.Build(
                    "export", 1, new { original = true, chinese = true, bilingual = true }, exportUpstreamKey);
                if (projectTracker is not null && exportStageKey is not null)
                {
                    activeStage = "export"; activeStageKey = exportStageKey;
                    await projectTracker.BeginAsync(activeStage, activeStageKey, cancellation.Token);
                }
                var translatedExporter = new TranslatedSrtExporter();
                await translatedExporter.ExportAsync(
                    result.Segments, translations, TranslatedSubtitleLayout.ChineseOnly,
                    chineseSrtPath, cancellation.Token);
                await translatedExporter.ExportAsync(
                    result.Segments, translations, TranslatedSubtitleLayout.OriginalThenChinese,
                    bilingualSrtPath, cancellation.Token);
                if (projectTracker is not null && exportStageKey is not null)
                {
                    await projectTracker.CompleteAsync(
                        "export", exportStageKey, [srtPath, chineseSrtPath, bilingualSrtPath], cancellation.Token);
                    activeStage = activeStageKey = null;
                }
                deepSeekClient?.Dispose();
            }

            var diagnostics = TranscriptionDiagnosticsAnalyzer.Analyze(result.Segments);
            var diagnosticsPath = Path.Combine(outputDirectory, "diagnostics.json");
            await File.WriteAllTextAsync(diagnosticsPath,
                JsonSerializer.Serialize(diagnostics, new JsonSerializerOptions { WriteIndented = true }),
                cancellation.Token);
            var retryWindows = RetryWindowPlanner.Plan(
                diagnostics,
                media.Duration,
                TimeSpan.FromMinutes(2),
                TimeSpan.FromSeconds(10));
            var retryPath = Path.Combine(outputDirectory, "retry-plan.json");
            await File.WriteAllTextAsync(retryPath,
                JsonSerializer.Serialize(retryWindows, new JsonSerializerOptions { WriteIndented = true }),
                cancellation.Token);

            var realtimeFactor = wallClock.Elapsed.TotalSeconds > 0
                ? media.Duration.TotalSeconds / wallClock.Elapsed.TotalSeconds
                : 0;
            Console.WriteLine();
            Console.WriteLine($"Segments: {result.Segments.Count}");
            Console.WriteLine($"Transcription time: {wallClock.Elapsed}");
            Console.WriteLine($"Speed: {realtimeFactor:0.00}x realtime");
            Console.WriteLine($"Repeated runs: {diagnostics.RepeatedRuns.Count} ({diagnostics.RepeatedSegmentCount} segments)");
            Console.WriteLine($"JSON: {jsonPath}");
            Console.WriteLine($"SRT:  {srtPath}");
            if (chineseSrtPath is not null)
            {
                Console.WriteLine($"Chinese SRT: {chineseSrtPath}");
                Console.WriteLine($"Bilingual SRT: {bilingualSrtPath}");
                Console.WriteLine($"QC report: {qcReportPath}");
            }
            Console.WriteLine($"Diagnostics: {diagnosticsPath}");
            Console.WriteLine($"Retry windows: {retryWindows.Count} ({retryPath})");
            if (projectDirectory is not null)
                Console.WriteLine($"Project: {Path.Combine(projectDirectory!, "project.json")}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            if (projectTracker is not null && activeStage is not null && activeStageKey is not null)
                await projectTracker.CancelAsync(activeStage, activeStageKey, CancellationToken.None);
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            if (projectTracker is not null && activeStage is not null && activeStageKey is not null)
                await projectTracker.FailAsync(activeStage, activeStageKey, exception, CancellationToken.None);
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static void PrintMediaInfo(MediaInfo media)
    {
        Console.WriteLine($"Media: {media.Path}");
        Console.WriteLine($"Duration: {media.Duration}");
        foreach (var track in media.AudioTracks)
            Console.WriteLine($"Audio stream {track.StreamIndex}: {track.Codec}, {track.Language ?? "unknown"}, {track.Channels} ch{(track.IsDefault ? ", default" : "")}");
    }

    private static string? ReadDeepSeekApiKey() =>
        LocalApiKeyResolver.ReadDeepSeekApiKey();

    private static void PrintHelp() => Console.WriteLine("""
        SubtitleTranslator benchmark

        Usage:
          dotnet run --project tools/SubtitleTranslator.Benchmark -- \
            --media <video> --model <ggml-model.bin> [options]

        Options:
          --stream <index>       Absolute ffprobe audio stream index
          --language <code>      Source language or auto (default: auto)
          --threads <count>      Whisper CPU thread count
          --no-context           Do not condition each decode window on previous text
          --chunk-minutes <n>    Split long audio into chunks (recommended: 5)
          --chunk-overlap <sec>  Context overlap around each chunk (default: 2)
          --vad-model <path>     Silero GGML model; writes detected speech regions
          --vad-threshold <n>    Speech probability threshold (default: 0.5)
          --vad-max-gap <sec>    Merge speech regions separated by this gap (default: 3)
          --no-retry             Disable diagnostic local retry after VAD transcription
          --test-translation     Generate clearly marked placeholder Chinese/bilingual SRT
          --deepseek-translation Translate with DeepSeek; reads DEEPSEEK_API_KEY from environment
          --deepseek-model <id>  DeepSeek model (default: deepseek-v4-flash)
          --translation-cache <directory>  Validated per-batch translation cache
          --translation-qa      Review only locally flagged ambiguous translations with DeepSeek
          --qc <mode>           Final subtitle QC: auto, suggest, off (default: auto)
          --project <directory> Persistent project manifest and stage cache
          --output <directory>   Output directory (default: benchmark-output)
          --ffmpeg <path>        FFmpeg executable (default: ffmpeg)
          --ffprobe <path>       FFprobe executable (default: ffprobe)
          --help                 Show help
        """);
}

internal sealed record CliOptions(
    string? MediaPath,
    string? ModelPath,
    int? StreamIndex,
    string Language,
    int? Threads,
    string? OutputDirectory,
    string FfmpegPath,
    string FfprobePath,
    bool NoContext,
    double? ChunkMinutes,
    double ChunkOverlapSeconds,
    string? VadModelPath,
    float VadThreshold,
    double VadMaximumGapSeconds,
    bool NoRetry,
    bool TestTranslation,
    bool DeepSeekTranslation,
    string DeepSeekModel,
    string? TranslationCacheDirectory,
    bool TranslationQa,
    string? ProjectDirectory,
    SubtitleQualityMode QualityMode,
    bool ShowHelp)
{
    public static CliOptions Parse(string[] args)
    {
        string? media = null, model = null, output = null;
        var language = "auto";
        var ffmpeg = "ffmpeg";
        var ffprobe = "ffprobe";
        int? stream = null, threads = null;
        var noContext = false;
        double? chunkMinutes = null;
        var chunkOverlapSeconds = 2d;
        string? vadModelPath = null;
        var vadThreshold = 0.5f;
        var vadMaximumGapSeconds = 3d;
        var noRetry = false;
        var testTranslation = false;
        var deepSeekTranslation = false;
        var deepSeekModel = "deepseek-v4-flash";
        string? translationCacheDirectory = null;
        var translationQa = false;
        string? projectDirectory = null;
        var qualityMode = SubtitleQualityMode.Auto;
        var help = args.Length == 0;

        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"Missing value for {args[i]}.");

            switch (args[i])
            {
                case "--media": media = Next(); break;
                case "--model": model = Next(); break;
                case "--stream": stream = int.Parse(Next()); break;
                case "--language": language = Next(); break;
                case "--threads": threads = int.Parse(Next()); break;
                case "--output": output = Next(); break;
                case "--ffmpeg": ffmpeg = Next(); break;
                case "--ffprobe": ffprobe = Next(); break;
                case "--no-context": noContext = true; break;
                case "--chunk-minutes": chunkMinutes = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--chunk-overlap": chunkOverlapSeconds = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--vad-model": vadModelPath = Next(); break;
                case "--vad-threshold": vadThreshold = float.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--vad-max-gap": vadMaximumGapSeconds = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--no-retry": noRetry = true; break;
                case "--test-translation": testTranslation = true; break;
                case "--deepseek-translation": deepSeekTranslation = true; break;
                case "--deepseek-model": deepSeekModel = Next(); break;
                case "--translation-cache": translationCacheDirectory = Next(); break;
                case "--translation-qa": translationQa = true; break;
                case "--project": projectDirectory = Next(); break;
                case "--qc": qualityMode = Enum.Parse<SubtitleQualityMode>(Next(), ignoreCase: true); break;
                case "--help" or "-h": help = true; break;
                default: throw new ArgumentException($"Unknown option: {args[i]}");
            }
        }

        if (testTranslation && deepSeekTranslation)
            throw new ArgumentException("--test-translation and --deepseek-translation cannot be used together.");
        if (translationQa && !deepSeekTranslation)
            throw new ArgumentException("--translation-qa requires --deepseek-translation.");
        return new CliOptions(media, model, stream, language, threads, output, ffmpeg, ffprobe, noContext, chunkMinutes, chunkOverlapSeconds, vadModelPath, vadThreshold, vadMaximumGapSeconds, noRetry, testTranslation, deepSeekTranslation, deepSeekModel, translationCacheDirectory, translationQa, projectDirectory, qualityMode, help);
    }
}

internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
