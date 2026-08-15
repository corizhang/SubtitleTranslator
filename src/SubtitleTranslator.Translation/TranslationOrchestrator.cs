using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class TranslationOrchestrator(ITranslationProvider provider)
{
    public async Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        IReadOnlyList<TranscriptSegment> transcript,
        string sourceLanguage,
        TranslationContext context,
        TranslationOptions options,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumSegmentsPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumCharactersPerBatch, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumAttemptsPerBatch, 1);

        var requests = transcript.Select(segment =>
            new TranslationRequestSegment(segment.Index, segment.Text.Trim())).ToArray();
        var batches = BuildBatches(requests, options);
        var translated = new List<TranslationSegment>(requests.Length);

        for (var index = 0; index < batches.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var items = batches[index];
            progress?.Report(new PipelineProgress(
                "translate", index * 100d / batches.Count,
                $"Translating batch {index + 1}/{batches.Count}"));
            IReadOnlyList<TranslationSegment>? validated = null;
            var accumulated = new Dictionary<int, TranslationSegment>();
            IReadOnlyList<TranslationRequestSegment> pending = items;
            Exception? lastError = null;
            for (var attempt = 1; attempt <= options.MaximumAttemptsPerBatch; attempt++)
            {
                try
                {
                    var response = await provider.TranslateAsync(
                        new TranslationBatch(pending, sourceLanguage), context, cancellationToken);
                    foreach (var translatedItem in TranslationResponseValidator.ValidatePartial(pending, response))
                        accumulated[translatedItem.SegmentId] = translatedItem;
                    pending = items.Where(item => !accumulated.ContainsKey(item.SegmentId)).ToArray();
                    if (pending.Count == 0)
                    {
                        validated = TranslationResponseValidator.ValidateAndOrder(items, accumulated.Values.ToArray());
                        if (provider is ICompleteTranslationBatchCache cache)
                            await cache.CacheCompleteAsync(
                                new TranslationBatch(items, sourceLanguage), context, validated, cancellationToken);
                        break;
                    }
                    lastError = new InvalidOperationException(
                        $"Translation response is missing {pending.Count} segment(s): {string.Join(", ", pending.Select(x => x.SegmentId))}.");
                    if (attempt < options.MaximumAttemptsPerBatch)
                    {
                        progress?.Report(new PipelineProgress(
                            "translate", index * 100d / batches.Count,
                            $"Batch {index + 1} missing {pending.Count} segment(s); repairing {string.Join(",", pending.Select(x => x.SegmentId))}"));
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                    }
                }
                catch (InvalidOperationException exception)
                {
                    lastError = exception;
                    if (attempt < options.MaximumAttemptsPerBatch)
                    {
                        progress?.Report(new PipelineProgress(
                            "translate", index * 100d / batches.Count,
                            $"Batch {index + 1} validation failed; retry {attempt + 1}/{options.MaximumAttemptsPerBatch}"));
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
                    }
                }
            }
            if (validated is null)
                throw new InvalidOperationException(
                    $"Translation batch {index + 1} failed validation after {options.MaximumAttemptsPerBatch} attempts.",
                    lastError);
            translated.AddRange(validated);
        }

        progress?.Report(new PipelineProgress("translate", 100, $"Translated {translated.Count} segments"));
        return translated;
    }

    internal static IReadOnlyList<IReadOnlyList<TranslationRequestSegment>> BuildBatches(
        IReadOnlyList<TranslationRequestSegment> segments,
        TranslationOptions options)
    {
        var result = new List<IReadOnlyList<TranslationRequestSegment>>();
        var current = new List<TranslationRequestSegment>();
        var characters = 0;
        foreach (var segment in segments)
        {
            var wouldOverflow = current.Count > 0 &&
                (current.Count >= options.MaximumSegmentsPerBatch ||
                 characters + segment.Text.Length > options.MaximumCharactersPerBatch);
            if (wouldOverflow)
            {
                result.Add(current.ToArray());
                current.Clear();
                characters = 0;
            }
            current.Add(segment);
            characters += segment.Text.Length;
        }
        if (current.Count > 0)
            result.Add(current.ToArray());
        return result;
    }
}
