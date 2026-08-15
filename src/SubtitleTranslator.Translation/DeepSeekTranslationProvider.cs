using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SubtitleTranslator.Application;
using SubtitleTranslator.Domain;

namespace SubtitleTranslator.Translation;

public sealed class DeepSeekTranslationProvider(
    HttpClient httpClient,
    DeepSeekTranslationOptions options) : ITranslationProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<TranslationSegment>> TranslateAsync(
        TranslationBatch batch,
        TranslationContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new InvalidOperationException("DeepSeek API key is required.");
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumAttempts, 1);

        Exception? lastError = null;
        for (var attempt = 1; attempt <= options.MaximumAttempts; attempt++)
        {
            try
            {
                using var request = BuildRequest(batch, context);
                using var response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var error = new DeepSeekApiException(response.StatusCode, SanitizeError(body));
                    if (!IsTransient(response.StatusCode) || attempt == options.MaximumAttempts)
                        throw error;
                    lastError = error;
                    await DelayAsync(response, attempt, cancellationToken);
                    continue;
                }

                return ParseResponse(body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < options.MaximumAttempts)
            {
                lastError = new TimeoutException("DeepSeek request timed out.");
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt < options.MaximumAttempts)
            {
                lastError = exception;
                await Task.Delay(Backoff(attempt), cancellationToken);
            }
        }

        throw lastError ?? new InvalidOperationException("DeepSeek translation failed.");
    }

    private HttpRequestMessage BuildRequest(TranslationBatch batch, TranslationContext context)
    {
        var endpoint = new Uri(new Uri(options.BaseUrl.TrimEnd('/') + "/"), "chat/completions");
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            model = options.Model,
            messages = new object[]
            {
                new { role = "system", content = BuildSystemPrompt(batch, context) },
                new { role = "user", content = BuildUserPrompt(batch) }
            },
            response_format = new { type = "json_object" },
            thinking = new { type = "disabled" },
            max_tokens = options.MaximumOutputTokens,
            stream = false
        }, options: JsonOptions);
        return request;
    }

    private static string BuildSystemPrompt(TranslationBatch batch, TranslationContext context)
    {
        var glossary = context.Glossary is { Count: > 0 }
            ? JsonSerializer.Serialize(context.Glossary, JsonOptions)
            : "{}";
        return $$"""
            You are a professional film and television subtitle translator.
            Translate from {{batch.SourceLanguage}} to {{batch.TargetLanguage}} using concise, natural spoken Chinese.
            Read neighboring segments as one continuous scene. Infer pronouns, omitted subjects, idioms, euphemisms,
            and implied meaning from context instead of translating isolated lines literally.
            Preserve meaning, tone, names, sound-effect notation, and continuity. Do not merge or split segments.
            Return JSON only in exactly this shape: {"translations":[{"segmentId":1,"text":"译文"}]}.
            Every input segmentId must occur exactly once. Do not add unknown IDs or empty translations.
            Title: {{context.Title ?? "unknown"}}
            Style: {{context.Style ?? "natural audiovisual subtitles"}}
            Glossary JSON: {{glossary}}
            """;
    }

    private static string BuildUserPrompt(TranslationBatch batch) =>
        "Translate this JSON segment array and return the required JSON object:\n" +
        JsonSerializer.Serialize(batch.Segments, JsonOptions);

    private static IReadOnlyList<TranslationSegment> ParseResponse(string body)
    {
        var envelope = JsonSerializer.Deserialize<ChatCompletionEnvelope>(body, JsonOptions)
            ?? throw new InvalidOperationException("DeepSeek returned an empty response envelope.");
        var choice = envelope.Choices?.FirstOrDefault()
            ?? throw new InvalidOperationException("DeepSeek response has no choices.");
        if (!string.Equals(choice.FinishReason, "stop", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"DeepSeek response ended with finish_reason '{choice.FinishReason}'.");
        var content = choice.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidOperationException("DeepSeek returned empty JSON content.");
        var payload = JsonSerializer.Deserialize<TranslationPayload>(content, JsonOptions)
            ?? throw new InvalidOperationException("DeepSeek returned an empty translation payload.");
        return payload.Translations ?? throw new InvalidOperationException("DeepSeek JSON has no translations array.");
    }

    private async Task DelayAsync(HttpResponseMessage response, int attempt, CancellationToken cancellationToken)
    {
        var retryAfter = response.Headers.RetryAfter?.Delta;
        await Task.Delay(
            retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero
                ? retryAfter.Value
                : Backoff(attempt),
            cancellationToken);
    }

    private TimeSpan Backoff(int attempt)
    {
        var multiplier = Math.Pow(2, attempt - 1);
        var jitter = Random.Shared.NextDouble() * 0.25 + 0.875;
        return TimeSpan.FromMilliseconds(options.RetryDelay.TotalMilliseconds * multiplier * jitter);
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;

    private static string SanitizeError(string body) =>
        body.Length <= 1000 ? body : body[..1000];

    private sealed record ChatCompletionEnvelope([property: JsonPropertyName("choices")] Choice[]? Choices);
    private sealed record Choice(
        [property: JsonPropertyName("finish_reason")] string? FinishReason,
        [property: JsonPropertyName("message")] Message? Message);
    private sealed record Message([property: JsonPropertyName("content")] string? Content);
    private sealed record TranslationPayload(
        [property: JsonPropertyName("translations")] TranslationSegment[]? Translations);
}

public sealed class DeepSeekApiException(HttpStatusCode statusCode, string responseBody)
    : Exception($"DeepSeek API returned HTTP {(int)statusCode} ({statusCode}): {responseBody}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
