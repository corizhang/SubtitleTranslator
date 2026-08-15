using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Application;

public sealed record SubtitleGenerationRequest(
    string MediaPath,
    string ModelPath,
    string VadModelPath,
    string ProjectDirectory,
    string OutputDirectory,
    string DeepSeekModel,
    SubtitleQualityMode QualityMode,
    bool TranslationQa = true,
    string SourceLanguage = "auto",
    string? DeepSeekApiKey = null,
    string? FfmpegPath = null,
    string? FfprobePath = null,
    string? NativeRuntimePath = null);

public sealed record SubtitleGenerationResult(
    string ProjectFile,
    string OriginalSubtitle,
    string ChineseSubtitle,
    string BilingualSubtitle,
    string QualityReport);
