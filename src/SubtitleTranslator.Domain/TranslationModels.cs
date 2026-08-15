namespace SubtitleTranslator.Domain;

public sealed record TranslationRequestSegment(int SegmentId, string Text);

public sealed record TranslationSegment(int SegmentId, string Text);

public sealed record TranslationBatch(
    IReadOnlyList<TranslationRequestSegment> Segments,
    string SourceLanguage,
    string TargetLanguage = "zh-CN");

public sealed record TranslationContext(
    string? Title = null,
    string? Style = null,
    IReadOnlyDictionary<string, string>? Glossary = null);

public sealed record TranslationOptions(
    int MaximumSegmentsPerBatch = 40,
    int MaximumCharactersPerBatch = 4000,
    int MaximumAttemptsPerBatch = 3);
