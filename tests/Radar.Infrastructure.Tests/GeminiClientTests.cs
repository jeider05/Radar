using System.Net;
using System.Text.Json;
using Radar.Core.Contents;
using Radar.Infrastructure.Gemini;

namespace Radar.Infrastructure.Tests;

public sealed class GeminiClientTests
{
    private const string TestApiKey = "test-key-not-real";

    private const string SuccessJson = """
        {
          "candidates": [
            { "content": { "role": "model", "parts": [ { "text": "Resumen: " }, { "text": "nueva versión." } ] }, "finishReason": "STOP" }
          ]
        }
        """;

    private static readonly ContentItem Release = new(
        7, 1, "222", "jq 1.8.2", "https://github.com/jqlang/jq/releases/tag/jq-1.8.2",
        "## Security\n- Fix CVE-2026-0001 in the parser.", new DateTime(2026, 6, 20, 14, 11, 27, DateTimeKind.Utc),
        new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc), IsRead: false);

    [Fact]
    public async Task ExplainReleaseAsync_returns_the_generated_text()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson));

        var text = await client.ExplainReleaseAsync("jqlang/jq", Release);

        Assert.Equal("Resumen: nueva versión.", text);
    }

    [Fact]
    public async Task Sends_key_in_header_never_in_url_to_the_configured_model()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson);

        await CreateClient(handler, model: "gemini-test-model").ExplainReleaseAsync("jqlang/jq", Release);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-test-model:generateContent",
            request.RequestUri!.ToString());
        Assert.Equal(TestApiKey, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(TestApiKey, request.RequestUri.ToString());
    }

    [Fact]
    public async Task Request_contains_the_release_notes_and_spanish_grounded_instructions_without_temperature()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson);

        await CreateClient(handler).ExplainReleaseAsync("jqlang/jq", Release);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        var system = body.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        var user = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("español", system);
        Assert.Contains("No inventes", system);
        Assert.Contains("jqlang/jq", user);
        Assert.Contains("jq 1.8.2", user);
        Assert.Contains("Fix CVE-2026-0001 in the parser.", user);
        Assert.DoesNotContain("temperature", body.RootElement.GetRawText());
    }

    [Fact]
    public async Task Very_long_notes_are_truncated_and_marked()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson);
        var longRelease = Release with { Description = new string('x', GeminiClient.MaxNotesLength + 500) };

        await CreateClient(handler).ExplainReleaseAsync("jqlang/jq", longRelease);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        var user = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.DoesNotContain(new string('x', GeminiClient.MaxNotesLength + 1), user);
        Assert.Contains("truncadas", user);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Release_without_notes_is_rejected_without_calling_the_api(string? notes)
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson);

        await Assert.ThrowsAsync<GeminiApiException>(
            () => CreateClient(handler).ExplainReleaseAsync("jqlang/jq", Release with { Description = notes }));

        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_errors_throw_GeminiApiException_with_status(HttpStatusCode status)
    {
        var client = CreateClient(new FakeHttpMessageHandler(status, """{"error":{"message":"details"}}"""));

        var ex = await Assert.ThrowsAsync<GeminiApiException>(() => client.ExplainReleaseAsync("jqlang/jq", Release));

        Assert.Equal(status, ex.StatusCode);
        Assert.DoesNotContain(TestApiKey, ex.Message);
    }

    [Theory]
    [InlineData("""{ "promptFeedback": { "blockReason": "SAFETY" } }""")]
    [InlineData("""{ "candidates": [ { "content": { "parts": [] }, "finishReason": "MAX_TOKENS" } ] }""")]
    [InlineData("""not json""")]
    public async Task Empty_blocked_or_invalid_responses_throw_GeminiApiException(string json)
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, json));

        await Assert.ThrowsAsync<GeminiApiException>(() => client.ExplainReleaseAsync("jqlang/jq", Release));
    }

    [Fact]
    public void FromEnvironment_returns_null_without_api_key()
    {
        Assert.Null(GeminiClient.FromEnvironment(new HttpClient(), _ => null));
        Assert.Null(GeminiClient.FromEnvironment(new HttpClient(), _ => "  "));
    }

    [Fact]
    public void FromEnvironment_uses_default_model_unless_overridden()
    {
        var withDefault = GeminiClient.FromEnvironment(new HttpClient(), name => name == "GEMINI_API_KEY" ? TestApiKey : null);
        var withOverride = GeminiClient.FromEnvironment(new HttpClient(), name => name == "GEMINI_API_KEY" ? TestApiKey : "gemini-other");

        Assert.Equal(GeminiClient.DefaultModel, withDefault!.Model);
        Assert.Equal("gemini-other", withOverride!.Model);
    }

    private static GeminiClient CreateClient(FakeHttpMessageHandler handler, string model = "gemini-test-model") =>
        new(new HttpClient(handler), TestApiKey, model);
}
