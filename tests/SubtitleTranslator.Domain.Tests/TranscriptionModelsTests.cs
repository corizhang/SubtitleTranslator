using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Domain.Tests;

public sealed class TranscriptionModelsTests
{
    [Fact]
    public void Options_DefaultToAutomaticLanguageDetection()
    {
        var options = new TranscriptionOptions("model.bin");
        Assert.Equal("auto", options.Language);
        Assert.False(options.TranslateToEnglish);
        Assert.False(options.NoContext);
    }
}
