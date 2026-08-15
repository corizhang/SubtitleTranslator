using System.Globalization;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Media;

public sealed class FfmpegAudioExtractor(string executable = "ffmpeg") : IAudioExtractor
{
    private readonly ProcessRunner _runner = new();

    public async Task<AudioArtifact> ExtractAsync(
        string mediaPath,
        int streamIndex,
        string outputPath,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
            throw new FileNotFoundException("Media file was not found.", mediaPath);

        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        progress?.Report(new PipelineProgress("extract", 0, "Extracting 16 kHz mono WAV audio"));

        TimeSpan? processed = null;
        var result = await _runner.RunAsync(executable,
        [
            "-hide_banner", "-nostdin", "-y",
            "-i", mediaPath,
            "-map", $"0:{streamIndex}",
            "-vn", "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le",
            "-progress", "pipe:2", "-nostats",
            fullOutputPath
        ], line =>
        {
            if (line.StartsWith("out_time_ms=", StringComparison.Ordinal) &&
                long.TryParse(line[12..], CultureInfo.InvariantCulture, out var microseconds))
            {
                processed = TimeSpan.FromTicks(microseconds * 10);
                progress?.Report(new PipelineProgress("extract", null, $"Processed {processed:hh\\:mm\\:ss}"));
            }
        }, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed: {result.StandardError.Trim()}");

        progress?.Report(new PipelineProgress("extract", 100, "Audio extraction completed"));
        return new AudioArtifact(fullOutputPath, processed, streamIndex);
    }
}

