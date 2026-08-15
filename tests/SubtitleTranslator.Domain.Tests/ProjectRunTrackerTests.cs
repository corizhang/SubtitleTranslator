using SubtitleTranslator.Domain;
using SubtitleTranslator.Infrastructure;

namespace SubtitleTranslator.Domain.Tests;

public sealed class ProjectRunTrackerTests
{
    [Fact]
    public async Task Tracker_PersistsRunningFailedAndCompletedStates()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"project-tracker-{Guid.NewGuid():N}");
        try
        {
            var source = new SourceFileFingerprint("video.mkv", 10, DateTime.UnixEpoch, "source-a");
            var tracker = await ProjectRunTracker.OpenAsync(
                directory, "test", source, CancellationToken.None);

            await tracker.BeginAsync("transcription", "key-a", CancellationToken.None);
            Assert.Equal(PipelineStageState.Running,
                (await new FileProjectStore(directory).LoadAsync(CancellationToken.None))!
                .Stages["transcription"].State);

            await tracker.FailAsync("transcription", "key-a", new InvalidOperationException("boom\nsecret detail"));
            var failed = (await new FileProjectStore(directory).LoadAsync(CancellationToken.None))!
                .Stages["transcription"];
            Assert.Equal(PipelineStageState.Failed, failed.State);
            Assert.DoesNotContain('\n', failed.Error!);

            await tracker.CompleteAsync("transcription", "key-a", ["transcript.json"], CancellationToken.None);
            var completed = (await new FileProjectStore(directory).LoadAsync(CancellationToken.None))!
                .Stages["transcription"];
            Assert.Equal(PipelineStageState.Completed, completed.State);
            Assert.Equal("transcript.json", Assert.Single(completed.Artifacts));

            await tracker.BeginAsync("translation", "key-b", CancellationToken.None);
            await tracker.CancelAsync("translation", "key-b");
            Assert.Equal(PipelineStageState.Cancelled,
                (await new FileProjectStore(directory).LoadAsync(CancellationToken.None))!
                .Stages["translation"].State);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_NewSourceCreatesNewProjectIdentityAndClearsStages()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"project-source-{Guid.NewGuid():N}");
        try
        {
            var first = await ProjectRunTracker.OpenAsync(directory, "test",
                new SourceFileFingerprint("a", 1, DateTime.UnixEpoch, "a"), CancellationToken.None);
            await first.CompleteAsync("audio", "key", ["audio.wav"], CancellationToken.None);
            var second = await ProjectRunTracker.OpenAsync(directory, "test",
                new SourceFileFingerprint("b", 1, DateTime.UnixEpoch, "b"), CancellationToken.None);

            Assert.NotEqual(first.Snapshot.ProjectId, second.Snapshot.ProjectId);
            Assert.Empty(second.Snapshot.Stages);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
