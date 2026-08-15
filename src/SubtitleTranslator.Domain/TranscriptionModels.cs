namespace SubtitleTranslator.Domain;

public sealed record TranscriptSegment(
    int Index,
    TimeSpan Start,
    TimeSpan End,
    string Text,
    string? Language = null,
    float? Confidence = null);

public sealed record TranscriptionResult(
    string Engine,
    string Model,
    string? Language,
    TimeSpan ProcessingTime,
    IReadOnlyList<TranscriptSegment> Segments);

public sealed record TranscriptionOptions(
    string ModelPath,
    string Language = "auto",
    bool TranslateToEnglish = false,
    int? Threads = null,
    bool NoContext = false,
    string? NativeRuntimePath = null);

public sealed record AudioChunk(
    string Path,
    TimeSpan MediaStart,
    TimeSpan Duration,
    TimeSpan CoreStart,
    TimeSpan CoreEnd,
    int Index,
    int Count);

public sealed record SpeechRegion(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;
}

public sealed record SpeechWindow(
    TimeSpan Start,
    TimeSpan End,
    IReadOnlyList<SpeechRegion> Regions,
    int Index,
    int Count)
{
    public TimeSpan Duration => End - Start;
}

public sealed record VoiceActivityOptions(
    string ModelPath,
    float Threshold = 0.5f,
    TimeSpan? MinimumSpeechDuration = null,
    TimeSpan? MinimumSilenceDuration = null,
    TimeSpan? SpeechPadding = null,
    bool UseGpu = false);

public sealed record PipelineProgress(
    string Stage,
    double? Percent = null,
    string? Message = null);
