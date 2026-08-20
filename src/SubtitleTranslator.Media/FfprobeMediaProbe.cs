using System.Globalization;
using System.Text.Json;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Media;

public sealed class FfprobeMediaProbe(string executable = "ffprobe") : IMediaProbe
{
    private readonly ProcessRunner _runner = new();

    public async Task<MediaInfo> ProbeAsync(string mediaPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaPath))
            throw new FileNotFoundException("Media file was not found.", mediaPath);

        var result = await _runner.RunAsync(executable,
        [
            "-v", "error",
            "-show_entries", "format=duration:stream=index,codec_type,codec_name,channels,sample_rate,width,height:stream_tags=language,title:stream_disposition=default",
            "-of", "json",
            mediaPath
        ], null, cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed: {result.StandardError.Trim()}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var duration = ParseDuration(root);
        var tracks = new List<AudioTrack>();
        int? videoWidth = null;
        int? videoHeight = null;

        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (GetString(stream, "codec_type") == "video" && videoWidth is null)
                {
                    videoWidth = GetInt32(stream, "width");
                    videoHeight = GetInt32(stream, "height");
                    continue;
                }
                if (GetString(stream, "codec_type") != "audio")
                    continue;

                var tags = stream.TryGetProperty("tags", out var tagValue) ? tagValue : default;
                var disposition = stream.TryGetProperty("disposition", out var dispositionValue) ? dispositionValue : default;
                tracks.Add(new AudioTrack(
                    stream.GetProperty("index").GetInt32(),
                    GetString(tags, "language"),
                    GetString(tags, "title"),
                    GetString(stream, "codec_name") ?? "unknown",
                    GetInt32(stream, "channels"),
                    GetInt32(stream, "sample_rate"),
                    disposition.ValueKind == JsonValueKind.Object && GetInt32(disposition, "default") == 1));
            }
        }

        return new MediaInfo(Path.GetFullPath(mediaPath), duration, tracks, videoWidth, videoHeight);
    }

    private static TimeSpan ParseDuration(JsonElement root)
    {
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var value) &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(seconds);
        return TimeSpan.Zero;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static int? GetInt32(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) &&
        int.TryParse(value.ToString(), CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
