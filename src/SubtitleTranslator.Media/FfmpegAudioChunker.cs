using System.Globalization;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Media;

public sealed class FfmpegAudioChunker(string executable = "ffmpeg") : IAudioChunker
{
    private readonly ProcessRunner _runner = new();

    public async Task<IReadOnlyList<AudioChunk>> SplitAsync(
        AudioArtifact audio,
        TimeSpan chunkDuration,
        TimeSpan overlap,
        string outputDirectory,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(audio.Path))
            throw new FileNotFoundException("Audio file was not found.", audio.Path);
        if (audio.Duration is not { } totalDuration || totalDuration <= TimeSpan.Zero)
            throw new ArgumentException("Audio duration is required for chunking.", nameof(audio));
        if (chunkDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(chunkDuration));
        if (overlap < TimeSpan.Zero || overlap >= chunkDuration / 2)
            throw new ArgumentOutOfRangeException(nameof(overlap));

        var chunkCount = (int)Math.Ceiling(totalDuration.TotalSeconds / chunkDuration.TotalSeconds);
        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullOutputDirectory);
        var chunks = new List<AudioChunk>(chunkCount);

        for (var index = 0; index < chunkCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var coreStart = TimeSpan.FromTicks(chunkDuration.Ticks * index);
            var coreEnd = Min(totalDuration, coreStart + chunkDuration);
            var mediaStart = Max(TimeSpan.Zero, coreStart - overlap);
            var mediaEnd = Min(totalDuration, coreEnd + overlap);
            var duration = mediaEnd - mediaStart;
            var outputPath = Path.Combine(fullOutputDirectory, $"chunk-{index:0000}.wav");

            progress?.Report(new PipelineProgress(
                "chunk",
                index * 100d / chunkCount,
                $"Creating audio chunk {index + 1}/{chunkCount}"));

            var result = await _runner.RunAsync(executable,
            [
                "-hide_banner", "-loglevel", "error", "-nostdin", "-y",
                "-ss", Format(mediaStart),
                "-i", audio.Path,
                "-t", Format(duration),
                "-ac", "1", "-ar", "16000", "-c:a", "pcm_s16le",
                outputPath
            ], null, cancellationToken);

            if (result.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg chunking failed: {result.StandardError.Trim()}");

            chunks.Add(new AudioChunk(
                outputPath, mediaStart, duration, coreStart, coreEnd, index, chunkCount));
        }

        progress?.Report(new PipelineProgress("chunk", 100, $"Created {chunkCount} audio chunks"));
        return chunks;
    }

    private static string Format(TimeSpan value) => value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
    private static TimeSpan Max(TimeSpan left, TimeSpan right) => left >= right ? left : right;
}

