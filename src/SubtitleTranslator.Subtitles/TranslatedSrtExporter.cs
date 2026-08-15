using System.Globalization;
using System.Text;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Subtitles;

public enum TranslatedSubtitleLayout
{
    ChineseOnly,
    OriginalThenChinese,
    ChineseThenOriginal
}

public sealed class TranslatedSrtExporter
{
    public async Task ExportAsync(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<TranslationSegment> translations,
        TranslatedSubtitleLayout layout,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var expected = transcript.Select(segment => segment.Index).ToHashSet();
        var duplicate = translations.GroupBy(item => item.SegmentId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Duplicate translation SegmentId {duplicate.Key}.");
        if (translations.Any(item => !expected.Contains(item.SegmentId)))
            throw new InvalidOperationException("Translations contain an unknown SegmentId.");

        var byId = translations.ToDictionary(item => item.SegmentId);
        var missing = transcript.FirstOrDefault(segment => !byId.ContainsKey(segment.Index));
        if (missing is not null)
            throw new InvalidOperationException($"Missing translation for SegmentId {missing.Index}.");

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await using var writer = new StreamWriter(
            fullPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        for (var index = 0; index < transcript.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = transcript[index];
            var translated = byId[source.Index].Text.Trim();
            await writer.WriteLineAsync((index + 1).ToString(CultureInfo.InvariantCulture));
            await writer.WriteLineAsync($"{SrtExporter.Format(source.Start)} --> {SrtExporter.Format(source.End)}");
            switch (layout)
            {
                case TranslatedSubtitleLayout.ChineseOnly:
                    await writer.WriteLineAsync(translated);
                    break;
                case TranslatedSubtitleLayout.OriginalThenChinese:
                    await writer.WriteLineAsync(source.Text.Trim());
                    await writer.WriteLineAsync(translated);
                    break;
                case TranslatedSubtitleLayout.ChineseThenOriginal:
                    await writer.WriteLineAsync(translated);
                    await writer.WriteLineAsync(source.Text.Trim());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(layout));
            }
            await writer.WriteLineAsync();
        }
    }
}
