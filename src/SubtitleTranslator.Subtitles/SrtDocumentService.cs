using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SubtitleTranslator.Subtitles;

public sealed partial class SrtDocumentService
{
    public async Task<IReadOnlyList<SubtitleCue>> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(Path.GetFullPath(path), cancellationToken);
        return Parse(content);
    }

    public IReadOnlyList<SubtitleCue> Parse(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('\uFEFF');
        var blocks = BlankLineRegex().Split(normalized.Trim());
        var result = new List<SubtitleCue>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length < 2) throw new FormatException("字幕块缺少时间轴或正文。");
            var timelineIndex = lines[0].Contains("-->", StringComparison.Ordinal) ? 0 : 1;
            if (lines.Length <= timelineIndex + 1)
                throw new FormatException("字幕块缺少正文。");
            var match = TimelineRegex().Match(lines[timelineIndex].Trim());
            if (!match.Success) throw new FormatException($"无法解析字幕时间轴：{lines[timelineIndex]}");
            var number = timelineIndex == 1 && int.TryParse(lines[0].Trim(), out var parsedNumber)
                ? parsedNumber : result.Count + 1;
            var text = string.Join(Environment.NewLine, lines.Skip(timelineIndex + 1)).Trim();
            result.Add(new SubtitleCue(number, ParseTimestamp(match.Groups[1].Value),
                ParseTimestamp(match.Groups[2].Value), text));
        }

        if (result.Count == 0) throw new FormatException("字幕文件中没有可用条目。");
        return result;
    }

    public IReadOnlyList<SubtitleCueIssue> Validate(IReadOnlyList<SubtitleCue> cues)
    {
        var issues = new List<SubtitleCueIssue>();
        for (var index = 0; index < cues.Count; index++)
        {
            var cue = cues[index];
            if (string.IsNullOrWhiteSpace(cue.Text))
                issues.Add(Issue(cue, SubtitleIssueSeverity.Error, "empty", "字幕正文为空"));
            if (cue.Start < TimeSpan.Zero)
                issues.Add(Issue(cue, SubtitleIssueSeverity.Error, "negative-start", "开始时间不能小于零"));
            if (cue.End <= cue.Start)
                issues.Add(Issue(cue, SubtitleIssueSeverity.Error, "invalid-duration", "结束时间必须晚于开始时间"));
            else
            {
                var duration = cue.End - cue.Start;
                if (duration < TimeSpan.FromMilliseconds(350))
                    issues.Add(Issue(cue, SubtitleIssueSeverity.Warning, "too-short", "显示时间短于 0.35 秒"));
                if (duration > TimeSpan.FromSeconds(12))
                    issues.Add(Issue(cue, SubtitleIssueSeverity.Warning, "too-long", "显示时间超过 12 秒"));
                var characters = cue.Text.Count(character => !char.IsWhiteSpace(character));
                if (characters / duration.TotalSeconds > 18)
                    issues.Add(Issue(cue, SubtitleIssueSeverity.Warning, "reading-speed", "文字较多，可能来不及阅读"));
            }
            if (index > 0 && cue.Start < cues[index - 1].End)
                issues.Add(Issue(cue, SubtitleIssueSeverity.Error, "overlap", $"与第 {cues[index - 1].Number} 条时间重叠"));
            if (cue.Text.Split('\n').Any(line => line.Trim().Length > 42))
                issues.Add(Issue(cue, SubtitleIssueSeverity.Warning, "long-line", "单行超过 42 个字符"));
        }
        return issues;
    }

    public async Task SaveAsync(string path, IReadOnlyList<SubtitleCue> cues, CancellationToken cancellationToken)
    {
        var errors = Validate(cues).Where(issue => issue.Severity == SubtitleIssueSeverity.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException($"字幕仍有 {errors.Length} 个错误，修正后才能保存。");
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var backupPath = fullPath + ".pre-edit.bak";
        if (File.Exists(fullPath) && !File.Exists(backupPath)) File.Copy(fullPath, backupPath);
        var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        await using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(true)))
        {
            for (var index = 0; index < cues.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cue = cues[index];
                await writer.WriteLineAsync((index + 1).ToString(CultureInfo.InvariantCulture));
                await writer.WriteLineAsync($"{SrtExporter.Format(cue.Start)} --> {SrtExporter.Format(cue.End)}");
                await writer.WriteLineAsync(cue.Text.Trim());
                await writer.WriteLineAsync();
            }
        }
        File.Move(temporary, fullPath, true);
    }

    public static bool TryParseTimestamp(string value, out TimeSpan result)
    {
        result = default;
        var match = TimestampRegex().Match(value.Trim());
        if (!match.Success) return false;
        var hours = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        if (minutes > 59 || seconds > 59) return false;
        result = new TimeSpan(0, hours, minutes, seconds,
            int.Parse(match.Groups[4].Value.PadRight(3, '0'), CultureInfo.InvariantCulture));
        return true;
    }

    public static string FormatTimestamp(TimeSpan value) => SrtExporter.Format(value);

    private static TimeSpan ParseTimestamp(string value) => TryParseTimestamp(value, out var result)
        ? result : throw new FormatException($"无法解析字幕时间：{value}");
    private static SubtitleCueIssue Issue(SubtitleCue cue, SubtitleIssueSeverity severity, string code, string message) =>
        new(cue.Number, severity, code, message);

    [GeneratedRegex(@"\n[ \t]*\n+")]
    private static partial Regex BlankLineRegex();
    [GeneratedRegex(@"^(\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})\s*-->\s*(\d{1,3}:\d{2}:\d{2}[,.]\d{1,3})")]
    private static partial Regex TimelineRegex();
    [GeneratedRegex(@"^(\d{1,3}):(\d{2}):(\d{2})[,.](\d{1,3})$")]
    private static partial Regex TimestampRegex();
}
