using System.Net;
using System.Text;
using SubtitleTranslator.Domain;
using SubtitleTranslator.Translation;

namespace SubtitleTranslator.Domain.Tests;

public sealed class DeepSeekTranslationProviderTests
{
    [Fact]
    public async Task TranslateAsync_ParsesStructuredJsonAndSendsBearerToken()
    {
        string? capturedScheme = null, capturedToken = null, capturedBody = null;
        var handler = new StubHandler(request =>
        {
            capturedScheme = request.Headers.Authorization?.Scheme;
            capturedToken = request.Headers.Authorization?.Parameter;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Response(HttpStatusCode.OK,
                """{"choices":[{"finish_reason":"stop","message":{"content":"{\"translations\":[{\"segmentId\":3,\"text\":\"你好\"}]}"}}]}""");
        });
        using var client = new HttpClient(handler);
        var provider = new DeepSeekTranslationProvider(client,
            new DeepSeekTranslationOptions("secret", MaximumAttempts: 1));

        var result = await provider.TranslateAsync(
            new TranslationBatch([new TranslationRequestSegment(3, "Hello")], "en"),
            new TranslationContext(), CancellationToken.None);

        Assert.Equal("你好", Assert.Single(result).Text);
        Assert.Equal("Bearer", capturedScheme);
        Assert.Equal("secret", capturedToken);
        Assert.Contains("deepseek-v4-flash", capturedBody);
        Assert.Contains("json_object", capturedBody);
        Assert.Contains("disabled", capturedBody);
    }

    [Fact]
    public async Task TranslateAsync_RetriesTransientServerError()
    {
        var calls = 0;
        var handler = new StubHandler(_ => ++calls == 1
            ? Response(HttpStatusCode.ServiceUnavailable, "busy")
            : Response(HttpStatusCode.OK,
                """{"choices":[{"finish_reason":"stop","message":{"content":"{\"translations\":[{\"segmentId\":1,\"text\":\"好\"}]}"}}]}"""));
        using var client = new HttpClient(handler);
        var provider = new DeepSeekTranslationProvider(client,
            new DeepSeekTranslationOptions("secret", MaximumAttempts: 2,
                InitialRetryDelay: TimeSpan.FromMilliseconds(1)));

        await provider.TranslateAsync(
            new TranslationBatch([new TranslationRequestSegment(1, "Good")], "en"),
            new TranslationContext(), CancellationToken.None);

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task TranslateAsync_DoesNotRetryAuthenticationFailure()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
        {
            calls++;
            return Response(HttpStatusCode.Unauthorized, "bad key");
        });
        using var client = new HttpClient(handler);
        var provider = new DeepSeekTranslationProvider(client,
            new DeepSeekTranslationOptions("secret", MaximumAttempts: 4));

        await Assert.ThrowsAsync<DeepSeekApiException>(() => provider.TranslateAsync(
            new TranslationBatch([new TranslationRequestSegment(1, "Good")], "en"),
            new TranslationContext(), CancellationToken.None));
        Assert.Equal(1, calls);
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
