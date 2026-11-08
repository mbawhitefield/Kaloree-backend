using System.Net;
using System.Text;
using System.Text.Json;
using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kaloree.Tests;

public sealed class ProviderTests
{
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AI:EncryptionKey"] = Convert.ToBase64String(new byte[32]), ["AI:ApiKey"] = "test-api-key",
        ["AI:Model"] = "test-vision-model", ["Billing:ApiKey"] = "test-billing-key", ["Billing:Entitlement"] = "premium"
    }).Build();

    [Fact]
    public async Task VisionUsesStructuredNonStoredImageRequestsAndReturnsReviewWarning()
    {
        var config = Config(); var cipher = new PhotoCipher(config); var owner = Guid.NewGuid();
        var handler = new StubHandler(async request =>
        {
            Assert.Equal("https://api.openai.com/v1/responses", request.RequestUri?.ToString());
            Assert.Equal("test-api-key", request.Headers.Authorization?.Parameter);
            using var sent = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.False(sent.RootElement.GetProperty("store").GetBoolean());
            Assert.True(sent.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());
            var estimate = """{"foods":[{"name":"Rice","grams":100,"calories":130,"protein":3,"carbs":28,"fat":0,"confidence":"medium"}],"questions":[],"warning":"generated warning"}""";
            return Json(new { status = "completed", output = new[] { new { type = "message", content = new[] { new { type = "output_text", text = estimate } } } } });
        });
        using var http = new HttpClient(handler);
        var service = new OpenAiVision(http, config, cipher);
        var result = await service.Analyze(new AnalysisJob { AccountId = owner, MimeType = "image/jpeg",
            EncryptedPhoto = cipher.Encrypt([255, 216, 255, 0], owner) }, default);
        Assert.Single(result.Foods);
        Assert.Contains("Confirm foods", result.Warning);
        Assert.DoesNotContain("generated warning", result.Warning);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(401, false)]
    public async Task VisionRetriesOnlyTransientProviderFailures(int status, bool retryable)
    {
        var config = Config(); var cipher = new PhotoCipher(config); var owner = Guid.NewGuid();
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status))));
        var error = await Assert.ThrowsAsync<VisionException>(() => new OpenAiVision(http, config, cipher)
            .Analyze(new AnalysisJob { AccountId = owner, MimeType = "image/jpeg", EncryptedPhoto = cipher.Encrypt([255, 216, 255], owner) }, default));
        Assert.Equal(retryable, error.Retryable);
    }

    [Fact]
    public async Task BillingUsesAuthoritativeGracePeriodExpiry()
    {
        var expiry = DateTimeOffset.UtcNow.AddDays(-1); var grace = DateTimeOffset.UtcNow.AddDays(3);
        using var http = new HttpClient(new StubHandler(_ => Task.FromResult(Json(new
        { subscriber = new { entitlements = new { premium = new { expires_date = expiry, grace_period_expires_date = grace } } } }))));
        Assert.Equal(grace, await new RevenueCat(http, Config()).PremiumExpiry(Guid.NewGuid(), default));
    }

    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK)
    { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response(request); }
}
