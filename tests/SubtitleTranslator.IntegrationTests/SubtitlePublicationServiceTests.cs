using SubtitleTranslator.Application;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.IntegrationTests;

public sealed class SubtitlePublicationServiceTests
{
    [Fact]
    public async Task Publish_UsesVideoNameAndWritesReceipt()
    {
        var root = NewDirectory();
        try
        {
            var media = await CreateAsync(root, "Episode 01.mkv", "video");
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var source = await CreateAsync(project, "bilingual.srt", "subtitle");
            var service = new SubtitlePublicationService();

            var receipt = await service.PublishAndRecordAsync(new SubtitlePublicationRequest(
                media, source, project, new SubtitlePublicationOptions()), CancellationToken.None);

            Assert.True(receipt.Success);
            Assert.Equal(Path.Combine(root, "Episode 01.zh-CN.bilingual.srt"), receipt.PublishedPath);
            Assert.Equal("subtitle", await File.ReadAllTextAsync(receipt.PublishedPath!));
            Assert.NotNull(await service.LoadReceiptAsync(project, CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Publish_BackupsExistingTargetBeforeOverwrite()
    {
        var root = NewDirectory();
        try
        {
            var media = await CreateAsync(root, "movie.mkv", "video");
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var source = await CreateAsync(project, "chinese.srt", "new");
            var target = await CreateAsync(root, "movie.srt", "old");
            var options = new SubtitlePublicationOptions(NamingStrategy: SubtitleNamingStrategy.SameAsVideo,
                Layout: "chinese");

            var receipt = await new SubtitlePublicationService().PublishAndRecordAsync(
                new SubtitlePublicationRequest(media, source, project, options), CancellationToken.None);

            Assert.True(receipt.Success);
            Assert.Equal("new", await File.ReadAllTextAsync(target));
            Assert.Equal("old", await File.ReadAllTextAsync(target + ".pre-publish.bak"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Publish_AutoNumbersWithoutOverwriting()
    {
        var root = NewDirectory();
        try
        {
            var media = await CreateAsync(root, "movie.mkv", "video");
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var source = await CreateAsync(project, "chinese.srt", "new");
            var existing = await CreateAsync(root, "movie.zh-CN.chinese.srt", "old");
            var options = new SubtitlePublicationOptions(ConflictPolicy: SubtitleConflictPolicy.AutoNumber, Layout: "chinese");

            var receipt = await new SubtitlePublicationService().PublishAndRecordAsync(
                new SubtitlePublicationRequest(media, source, project, options), CancellationToken.None);

            Assert.Equal("old", await File.ReadAllTextAsync(existing));
            Assert.EndsWith("movie.zh-CN.chinese (2).srt", receipt.PublishedPath);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void BuildTargetPath_ExpandsCustomTemplateAndRejectsUnknownToken()
    {
        var service = new SubtitlePublicationService();
        var request = new SubtitlePublicationRequest(@"C:\Videos\Show.mkv", @"C:\Project\subtitle.srt", @"C:\Project",
            new SubtitlePublicationOptions(NamingStrategy: SubtitleNamingStrategy.CustomTemplate,
                NamingTemplate: "{video-name}.{language}.{layout}.custom"));
        Assert.EndsWith(@"Show.zh-CN.bilingual.custom.srt", service.BuildTargetPath(request));
        Assert.Throws<InvalidOperationException>(() => service.BuildTargetPath(request with
        { Options = request.Options with { NamingTemplate = "{unknown}.srt" } }));
    }

    [Fact]
    public async Task PublishFailure_IsRecordedWithoutThrowingAwayInternalSubtitle()
    {
        var root = NewDirectory();
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
            var source = await CreateAsync(project, "bilingual.srt", "completed subtitle");

            var receipt = await new SubtitlePublicationService().PublishAndRecordAsync(
                new SubtitlePublicationRequest(Path.Combine(root, "missing.mkv"), source, project,
                    new SubtitlePublicationOptions()), CancellationToken.None);

            Assert.False(receipt.Success);
            Assert.True(File.Exists(source));
            Assert.Contains("发布失败", receipt.Message);
            Assert.True(File.Exists(Path.Combine(project, "publication.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"subtitle-publish-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static async Task<string> CreateAsync(string directory, string name, string content)
    {
        var path = Path.Combine(directory, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
