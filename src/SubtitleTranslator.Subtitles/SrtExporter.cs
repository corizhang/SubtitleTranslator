using System.Globalization;
using System.Text;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Subtitles;

public sealed class SrtExporter : ISubtitleExporter
{
    public async Task ExportAsync(
        IReadOnlyList<TranscriptSegment> segments,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var writer = new StreamWriter(fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        for (var i = 0; i < segments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segment = segments[i];
            await writer.WriteLineAsync((i + 1).ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync($"{Format(segment.Start)} --> {Format(segment.End)}");
            await writer.WriteLineAsync(segment.Text.Trim());
            await writer.WriteLineAsync();
        }
    }

    internal static string Format(TimeSpan value)
    {
        var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value;
        var hours = (int)clamped.TotalHours;
        return $"{hours:00}:{clamped.Minutes:00}:{clamped.Seconds:00},{clamped.Milliseconds:000}";
    }
}

