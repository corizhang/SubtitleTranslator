namespace SubtitleTranslator.Domain;

public enum SubtitleQualityMode
{
    Auto,
    Suggest,
    Off
}

public enum SubtitleQualitySeverity
{
    Info,
    Warning,
    Error
}

public sealed record SubtitleQualityIssue(
    int SegmentId,
    string Code,
    SubtitleQualitySeverity Severity,
    string Message,
    string OriginalText,
    string? SuggestedText,
    bool AutoFixable,
    bool Applied);

public sealed record SubtitleQualityReport(
    SubtitleQualityMode Mode,
    int SegmentCount,
    int IssueCount,
    int AppliedFixCount,
    int OptionalConfirmationCount,
    IReadOnlyList<SubtitleQualityIssue> Issues);

public sealed record SubtitleQualityResult(
    IReadOnlyList<TranslationSegment> Translations,
    SubtitleQualityReport Report);
