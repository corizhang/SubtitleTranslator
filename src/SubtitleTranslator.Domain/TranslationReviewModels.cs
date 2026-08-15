namespace SubtitleTranslator.Domain;

public sealed record TranslationReviewCandidate(
    int SegmentId,
    string SourceText,
    string CurrentTranslation,
    string Reason,
    IReadOnlyList<TranslationReviewContextLine> Context);

public sealed record TranslationReviewContextLine(
    int SegmentId,
    string SourceText,
    string Translation,
    bool IsTarget);

public sealed record TranslationReviewResult(
    int SegmentId,
    string Text,
    bool Changed,
    string? Explanation = null);
