using SubtitleTranslator.Domain;
using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Domain.Tests;

public sealed class TranslationResponseValidatorTests
{
    [Fact]
    public void ValidateAndOrder_OrdersByRequestSegmentId()
    {
        TranslationRequestSegment[] request = [new(7, "a"), new(9, "b")];
        TranslationSegment[] response = [new(9, "乙"), new(7, "甲")];

        var result = TranslationResponseValidator.ValidateAndOrder(request, response);

        Assert.Equal([7, 9], result.Select(item => item.SegmentId));
        Assert.Equal(["甲", "乙"], result.Select(item => item.Text));
    }

    [Fact]
    public async Task Orchestrator_RetriesStructurallyInvalidBatch()
    {
        TranscriptSegment[] transcript =
        [new(0, TimeSpan.Zero, TimeSpan.FromSeconds(1), "a"),
         new(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "b")];
        var provider = new MissingOnceProvider();

        var result = await new TranslationOrchestrator(provider).TranslateAsync(
            transcript, "en", new TranslationContext(),
            new TranslationOptions(MaximumAttemptsPerBatch: 2), null, CancellationToken.None);

        Assert.Equal(2, provider.Calls);
        Assert.Equal([[0, 1], [1]], provider.RequestedIds);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ValidateAndOrder_RejectsMissingSegment()
    {
        TranslationRequestSegment[] request = [new(1, "a"), new(2, "b")];
        TranslationSegment[] response = [new(1, "甲")];

        var error = Assert.Throws<InvalidOperationException>(() =>
            TranslationResponseValidator.ValidateAndOrder(request, response));

        Assert.Contains("missing SegmentId 2", error.Message);
    }

    [Fact]
    public void ValidateAndOrder_RejectsDuplicateSegment()
    {
        TranslationRequestSegment[] request = [new(1, "a")];
        TranslationSegment[] response = [new(1, "甲"), new(1, "乙")];

        Assert.Throws<InvalidOperationException>(() =>
            TranslationResponseValidator.ValidateAndOrder(request, response));
    }

    private sealed class MissingOnceProvider : SubtitleTranslator.Application.ITranslationProvider
    {
        public int Calls { get; private set; }
        public List<int[]> RequestedIds { get; } = [];

        public Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
            TranslationBatch batch, TranslationContext context, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedIds.Add(batch.Segments.Select(x => x.SegmentId).ToArray());
            IReadOnlyList<TranslationSegment> result = (Calls == 1 ? batch.Segments.Take(1) : batch.Segments)
                .Select(item => new TranslationSegment(item.SegmentId, $"译:{item.Text}"))
                .ToArray();
            return Task.FromResult(result);
        }
    }
}
