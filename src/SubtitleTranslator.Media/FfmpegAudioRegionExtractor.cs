using System.Globalization;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Media;

public sealed class FfmpegAudioRegionExtractor(string executable = "ffmpeg") : IAudioRegionExtractor
{
    private readonly ProcessRunner _runner = new();

    public async Task<AudioArtifact> ExtractAsync(
        AudioArtifact source,
        TimeSpan start,
        TimeSpan duration,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(duration));

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var result = await _runner.RunAsync(executable,
        [
            "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
            "-ss", Format(start), "-i", source.Path, "-t", Format(duration),
            "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le", fullPath
        ], null, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg region extraction failed: {result.StandardError.Trim()}");
        return new AudioArtifact(fullPath, duration, source.SourceStreamIndex);
    }

    private static string Format(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}

