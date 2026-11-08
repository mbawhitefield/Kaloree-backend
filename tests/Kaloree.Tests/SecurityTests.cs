using System.Security.Cryptography;
using System.Text;
using Kaloree.Api.Api;
using Kaloree.Api.Domain;
using Kaloree.Api.Infrastructure;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Kaloree.Tests;

public sealed class SecurityTests
{
    [Theory]
    [InlineData("male", 1780)]
    [InlineData("female", 1614)]
    public void BmrUsesExpectedEquation(string sex, int expected) => Assert.Equal(expected, Nutrition.Bmr(80, 180, 30, sex));

    [Theory]
    [InlineData("2000-11-09", "2026-11-08", 25)]
    [InlineData("2000-11-08", "2026-11-08", 26)]
    [InlineData("2000-02-29", "2026-02-28", 25)]
    public void AgeUsesBirthDay(string birth, string today, int age) => Assert.Equal(age, Nutrition.Age(DateOnly.Parse(birth), DateOnly.Parse(today)));

    [Fact]
    public void NutritionScalesPortions() => Assert.Equal(247.5m, Nutrition.Scale(165, 150));

    [Fact]
    public void PhotoCipherAuthenticatesOwnerAndTampering()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AI:EncryptionKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }).Build();
        var cipher = new PhotoCipher(config); var owner = Guid.NewGuid(); var plain = Encoding.UTF8.GetBytes("private photo");
        var encrypted = cipher.Encrypt(plain, owner);
        Assert.Equal(plain, cipher.Decrypt(encrypted, owner));
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, Guid.NewGuid()));
        encrypted[^1] ^= 1;
        Assert.Throws<AuthenticationTagMismatchException>(() => cipher.Decrypt(encrypted, owner));
    }

    [Fact]
    public void WebhookRejectsTamperingAndReplay()
    {
        var now = DateTimeOffset.UtcNow; var body = Encoding.UTF8.GetBytes("{\"event\":{}}"); const string secret = "test-secret";
        var signature = Sign(body, secret, now);
        Assert.True(WebhookSignature.Verify(signature, body, secret, now));
        Assert.False(WebhookSignature.Verify(signature, Encoding.UTF8.GetBytes("{}"), secret, now));
        Assert.False(WebhookSignature.Verify(signature, body, "wrong", now));
        Assert.False(WebhookSignature.Verify(signature, body, secret, now.AddMinutes(6)));
        Assert.False(WebhookSignature.Verify("t=oops,v1=bad", body, secret, now));
        Assert.False(WebhookSignature.Verify($"t={long.MinValue},v1={new string('0', 64)}", body, secret, now));
    }

    public static string Sign(byte[] body, string secret, DateTimeOffset now)
    {
        var timestamp = now.ToUnixTimeSeconds();
        var raw = Encoding.UTF8.GetBytes(timestamp + ".").Concat(body).ToArray();
        return $"t={timestamp},v1={Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), raw)).ToLowerInvariant()}";
    }

    [Fact]
    public void ArbitraryBytesCannotBeSubmittedAsJpeg() => Assert.False(AnalysisEndpoints.IsImage("not an image"u8.ToArray(), "image/jpeg"));

    [Fact]
    public void VisionOutputMustBeBounded()
    {
        var food = new PhotoFood("Rice", 100, 130, 3, 28, 0, "medium");
        OpenAiVision.Validate(new PhotoEstimate([food], [], "estimate"));
        Assert.Throws<VisionException>(() => OpenAiVision.Validate(new PhotoEstimate([food with { Calories = -1 }], [], "")));
        Assert.Throws<VisionException>(() => OpenAiVision.Validate(new PhotoEstimate([food with { Confidence = "certain" }], [], "")));
        Assert.Throws<VisionException>(() => OpenAiVision.Validate(new PhotoEstimate(Enumerable.Repeat(food, 31).ToArray(), [], "")));
    }

    [Fact]
    public void DiaryRejectsOversizedAndNegativeEntries()
    {
        Assert.Throws<ApiException>(() => Check.Items([]));
        Assert.Throws<ApiException>(() => Check.Items([new ItemInput(null, "rice", 100, -1, 0, 0, 0)]));
        Assert.Throws<ApiException>(() => Check.Range(new DateOnly(2026, 1, 1), new DateOnly(2028, 1, 1)));
    }
}
