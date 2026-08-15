namespace SubtitleTranslator.Subtitles;

public sealed record SubtitleCue(int Number, TimeSpan Start, TimeSpan End, string Text);

public enum SubtitleIssueSeverity { Warning, Error }

public sealed record SubtitleCueIssue(
    int CueNumber,
    SubtitleIssueSeverity Severity,
    string Code,
    string Message);

