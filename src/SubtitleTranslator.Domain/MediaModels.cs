namespace SubtitleTranslator.Domain;

public sealed record AudioTrack(
    int StreamIndex,
    string? Language,
    string? Title,
    string Codec,
    int? Channels,
    int? SampleRate,
    bool IsDefault);

public sealed record MediaInfo(
    string Path,
    TimeSpan Duration,
    IReadOnlyList<AudioTrack> AudioTracks,
    int? VideoWidth = null,
    int? VideoHeight = null);

public sealed record AudioArtifact(
    string Path,
    TimeSpan? Duration,
    int SourceStreamIndex);
