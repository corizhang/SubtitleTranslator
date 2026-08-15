using System.Text.RegularExpressions;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Subtitles;

public sealed partial class FinalSubtitleQualityProcessor
{
    private static readonly string[] HighConfidenceDuplicatePatterns =
    ["没没"];

    public SubtitleQualityResult Process(
        IReadOnlyList<TranscriptSegment> transcript,
        IReadOnlyList<TranslationSegment> translations,
        SubtitleQualityMode mode)
    {
        if (mode == SubtitleQualityMode.Off)
            return new SubtitleQualityResult(
                translations,
                new SubtitleQualityReport(mode, transcript.Count, 0, 0, 0, []));

        var byId = translations.ToDictionary(item => item.SegmentId);
        var issues = new List<SubtitleQualityIssue>();
        var output = new List<TranslationSegment>(translations.Count);

        foreach (var source in transcript)
        {
            if (!byId.TryGetValue(source.Index, out var translation))
                throw new InvalidOperationException($"Missing translation for SegmentId {source.Index}.");
            var original = translation.Text.Trim();
            var fixedText = original;

            if (original.Length == 0)
                issues.Add(Issue(source.Index, "empty-translation", SubtitleQualitySeverity.Error,
                    "译文为空。", original, null, false, false));

            var whitespaceFixed = MultiSpaceRegex().Replace(fixedText, " ").Trim();
            if (!string.Equals(whitespaceFixed, fixedText, StringComparison.Ordinal))
            {
                var apply = mode == SubtitleQualityMode.Auto;
                issues.Add(Issue(source.Index, "repeated-whitespace", SubtitleQualitySeverity.Info,
                    "包含重复空格。", fixedText, whitespaceFixed, true, apply));
                if (apply) fixedText = whitespaceFixed;
            }

            var punctuationFixed = RepeatedPunctuationRegex().Replace(fixedText, "$1");
            if (!string.Equals(punctuationFixed, fixedText, StringComparison.Ordinal))
            {
                var apply = mode == SubtitleQualityMode.Auto;
                issues.Add(Issue(source.Index, "repeated-punctuation", SubtitleQualitySeverity.Info,
                    "包含重复中文标点。", fixedText, punctuationFixed, true, apply));
                if (apply) fixedText = punctuationFixed;
            }

            foreach (var pattern in HighConfidenceDuplicatePatterns)
            {
                if (!fixedText.Contains(pattern, StringComparison.Ordinal))
                    continue;
                var suggestion = fixedText.Replace(pattern, pattern[..1], StringComparison.Ordinal);
                var apply = mode == SubtitleQualityMode.Auto;
                issues.Add(Issue(source.Index, "likely-duplicate-character", SubtitleQualitySeverity.Warning,
                    $"疑似误重复“{pattern}”。", fixedText, suggestion, true, apply));
                if (apply) fixedText = suggestion;
            }

            var duration = source.End - source.Start;
            var visibleCharacters = VisibleCharacterRegex().Matches(fixedText).Count;
            var charactersPerSecond = duration.TotalSeconds > 0
                ? visibleCharacters / duration.TotalSeconds
                : double.PositiveInfinity;
            if (charactersPerSecond > 12)
                issues.Add(Issue(source.Index, "high-reading-speed", SubtitleQualitySeverity.Warning,
                    $"阅读速度较高：{charactersPerSecond:0.0} 字/秒。", fixedText, null, false, false));
            if (duration < TimeSpan.FromMilliseconds(800))
                issues.Add(Issue(source.Index, "short-duration", SubtitleQualitySeverity.Warning,
                    $"显示时间过短：{duration.TotalMilliseconds:0} 毫秒。", fixedText, null, false, false));

            var latinLetters = LatinLetterRegex().Matches(fixedText).Count;
            if (latinLetters >= 8 && latinLetters * 2 > Math.Max(1, visibleCharacters))
                issues.Add(Issue(source.Index, "possible-untranslated-text", SubtitleQualitySeverity.Warning,
                    "译文中包含较多拉丁字母，可能存在漏译。", fixedText, null, false, false));

            output.Add(translation with { Text = mode == SubtitleQualityMode.Auto ? fixedText : original });
        }

        var report = new SubtitleQualityReport(
            mode,
            transcript.Count,
            issues.Count,
            issues.Count(item => item.Applied),
            issues.Count(item => !item.Applied),
            issues);
        return new SubtitleQualityResult(output, report);
    }

    private static SubtitleQualityIssue Issue(
        int segmentId, string code, SubtitleQualitySeverity severity, string message,
        string original, string? suggestion, bool autoFixable, bool applied) =>
        new(segmentId, code, severity, message, original, suggestion, autoFixable, applied);

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"([，。！？；：])\1+")]
    private static partial Regex RepeatedPunctuationRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]")]
    private static partial Regex VisibleCharacterRegex();

    [GeneratedRegex("[A-Za-z]")]
    private static partial Regex LatinLetterRegex();
}
