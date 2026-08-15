using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public static class TranslationResponseValidator
{
    public static IReadOnlyList<TranslationSegment> ValidatePartial(
        IReadOnlyList<TranslationRequestSegment> request,
        IReadOnlyList<TranslationSegment> response)
    {
        var expected = request.Select(item => item.SegmentId).ToHashSet();
        var duplicate = response.GroupBy(item => item.SegmentId).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Translation response contains duplicate SegmentId {duplicate.Key}.");
        var unknown = response.FirstOrDefault(item => !expected.Contains(item.SegmentId));
        if (unknown is not null)
            throw new InvalidOperationException($"Translation response contains unknown SegmentId {unknown.SegmentId}.");
        var empty = response.FirstOrDefault(item => string.IsNullOrWhiteSpace(item.Text));
        if (empty is not null)
            throw new InvalidOperationException($"Translation response has empty text for SegmentId {empty.SegmentId}.");
        return response.Select(x => x with { Text = x.Text.Trim() }).ToArray();
    }

    public static IReadOnlyList<TranslationSegment> ValidateAndOrder(
        IReadOnlyList<TranslationRequestSegment> request,
        IReadOnlyList<TranslationSegment> response)
    {
        var expected = request.Select(item => item.SegmentId).ToHashSet();
        if (expected.Count != request.Count)
            throw new InvalidOperationException("Translation request contains duplicate SegmentId values.");

        response = ValidatePartial(request, response);

        var byId = response.ToDictionary(item => item.SegmentId);
        var missing = request.FirstOrDefault(item => !byId.ContainsKey(item.SegmentId));
        if (missing is not null)
            throw new InvalidOperationException($"Translation response is missing SegmentId {missing.SegmentId}.");

        return request.Select(item => byId[item.SegmentId] with { Text = byId[item.SegmentId].Text.Trim() }).ToArray();
    }
}
