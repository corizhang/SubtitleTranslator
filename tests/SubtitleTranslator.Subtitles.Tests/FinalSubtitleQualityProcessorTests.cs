using SubtitleTranslator.Domain;
using SubtitleTranslator.Subtitles;

namespace SubtitleTranslator.Subtitles.Tests;

public sealed class FinalSubtitleQualityProcessorTests
{
    [Fact]
    public void Process_AutoFixesHighConfidenceTextDefects()
    {
        TranscriptSegment[] source =
        [new(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Let's go before it runs out of fuel.")];
        TranslationSegment[] translation = [new(0, "趁着还没没油，  我们快走吧！！")];

        var result = new FinalSubtitleQualityProcessor().Process(
            source, translation, SubtitleQualityMode.Auto);

        Assert.Equal("趁着还没油， 我们快走吧！", Assert.Single(result.Translations).Text);
        Assert.Equal(3, result.Report.AppliedFixCount);
        Assert.All(result.Report.Issues.Where(item => item.AutoFixable), item => Assert.True(item.Applied));
    }

    [Fact]
    public void Process_SuggestReportsButDoesNotModify()
    {
        TranscriptSegment[] source =
        [new(0, TimeSpan.Zero, TimeSpan.FromSeconds(2), "text")];
        TranslationSegment[] translation = [new(0, "还没没油！！")];

        var result = new FinalSubtitleQualityProcessor().Process(
            source, translation, SubtitleQualityMode.Suggest);

        Assert.Equal("还没没油！！", Assert.Single(result.Translations).Text);
        Assert.Equal(0, result.Report.AppliedFixCount);
        Assert.True(result.Report.OptionalConfirmationCount >= 2);
    }

    [Fact]
    public void Process_OffSkipsAnalysis()
    {
        TranscriptSegment[] source =
        [new(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), "text")];
        TranslationSegment[] translation = [new(0, "还没没油！！")];

        var result = new FinalSubtitleQualityProcessor().Process(
            source, translation, SubtitleQualityMode.Off);

        Assert.Empty(result.Report.Issues);
        Assert.Same(translation, result.Translations);
    }
}
