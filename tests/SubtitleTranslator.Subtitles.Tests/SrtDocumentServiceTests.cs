using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.Subtitles.Tests;

public sealed class SrtDocumentServiceTests
{
    [Fact]
    public void Parse_SupportsMultilineAndDotMilliseconds()
    {
        var cues = new SrtDocumentService().Parse("1\r\n00:00:01,200 --> 00:00:03.400\r\nHello\r\n你好\r\n");

        var cue = Assert.Single(cues);
        Assert.Equal(TimeSpan.FromMilliseconds(1200), cue.Start);
        Assert.Equal("Hello" + Environment.NewLine + "你好", cue.Text);
    }

    [Fact]
    public void Validate_FindsOverlapInvalidDurationAndLongLine()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), "正常"),
            new SubtitleCue(2, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), new string('字', 43))
        };

        var issues = new SrtDocumentService().Validate(cues);

        Assert.Contains(issues, x => x.Code == "overlap");
        Assert.Contains(issues, x => x.Code == "invalid-duration");
        Assert.Contains(issues, x => x.Code == "long-line");
    }

    [Fact]
    public async Task SaveAsync_RenumbersAndRoundTripsUtf8()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-{Guid.NewGuid():N}.srt");
        try
        {
            var service = new SrtDocumentService();
            await service.SaveAsync(path,
                [new SubtitleCue(9, TimeSpan.Zero, TimeSpan.FromSeconds(2), "中文字幕")], CancellationToken.None);

            var loaded = await service.LoadAsync(path, CancellationToken.None);
            Assert.Equal("中文字幕", Assert.Single(loaded).Text);
            Assert.StartsWith("1", (await File.ReadAllTextAsync(path)).TrimStart('\uFEFF'));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task SaveAsync_PreservesOriginalOnceBeforeOverwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-{Guid.NewGuid():N}.srt");
        try
        {
            await File.WriteAllTextAsync(path, "original");
            var service = new SrtDocumentService();
            var cue = new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(2), "第一次修改");

            await service.SaveAsync(path, [cue], CancellationToken.None);
            await service.SaveAsync(path, [cue with { Text = "第二次修改" }], CancellationToken.None);

            Assert.Equal("original", await File.ReadAllTextAsync(path + ".pre-edit.bak"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".pre-edit.bak")) File.Delete(path + ".pre-edit.bak");
        }
    }

    [Theory]
    [InlineData("00:60:00,000")]
    [InlineData("00:00:60,000")]
    [InlineData("not-a-time")]
    public void TryParseTimestamp_RejectsInvalidValues(string value) =>
        Assert.False(SrtDocumentService.TryParseTimestamp(value, out _));
}
