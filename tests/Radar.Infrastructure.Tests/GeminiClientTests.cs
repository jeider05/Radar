using System.Net;
using System.Text.Json;
using Radar.Core.Learning;
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

    [Fact]
    public async Task Sends_key_in_header_never_in_url_to_the_configured_model()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, SuccessJson);
        using var schema = JsonDocument.Parse("""{"type":"OBJECT"}""");

        await CreateClient(handler, model: "gemini-test-model").GenerateJsonAsync("Sistema", "Usuario", schema.RootElement, "json");

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-test-model:generateContent",
            request.RequestUri!.ToString());
        Assert.Equal(TestApiKey, request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain(TestApiKey, request.RequestUri.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Http_errors_throw_GeminiApiException_with_status(HttpStatusCode status)
    {
        var client = CreateClient(new FakeHttpMessageHandler(status, """{"error":{"message":"details"}}"""));
        using var schema = JsonDocument.Parse("""{"type":"OBJECT"}""");

        var ex = await Assert.ThrowsAsync<GeminiApiException>(() => client.GenerateJsonAsync("Sistema", "Usuario", schema.RootElement, "json"));

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
        using var schema = JsonDocument.Parse("""{"type":"OBJECT"}""");

        await Assert.ThrowsAsync<GeminiApiException>(() => client.GenerateJsonAsync("Sistema", "Usuario", schema.RootElement, "json"));
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

    private static readonly StudyTopic Topic = new(
        3, "engram", "Componentes", "Engram", 6, "Qué es", "Para qué", "Cómo", "Ejemplo", "Diferencias",
        "- La memoria local en SQLite es la fuente predeterminada.", "https://github.com/Gentleman-Programming/gentle-ai/blob/main/docs/engram.md",
        "Consultada el 2026-10-27");

    private static string QuestionsJson(string questionsArray) =>
        JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text = questionsArray } } } } } });

    [Fact]
    public async Task GenerateStudyQuestionsAsync_returns_valid_questions_with_gemini_origin_and_topic()
    {
        var json = QuestionsJson("""
            {"questions":[{"kind":"Scenario","difficulty":"Advanced","prompt":"¿Dónde guarda Engram?","options":["Nube","SQLite local","Chat"],"correctOption":1,"explanation":"La memoria local en SQLite es la fuente predeterminada."}]}
            """);
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, json), model: "gemini-test-model");

        var question = Assert.Single(await client.GenerateStudyQuestionsAsync(Topic, 3, []));

        Assert.Equal(Topic.Id, question.TopicId);
        Assert.Equal(QuestionKind.Scenario, question.Kind);
        Assert.Equal(QuestionDifficulty.Advanced, question.Difficulty);
        Assert.Equal(["Nube", "SQLite local", "Chat"], question.Options);
        Assert.Equal(1, question.CorrectOption);
        Assert.Equal("Gemini gemini-test-model", question.Origin);
    }

    [Fact]
    public async Task GenerateStudyQuestionsAsync_grounds_the_request_in_the_topic_facts_and_asks_for_json()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, QuestionsJson("""{"questions":[]}"""));

        await CreateClient(handler).GenerateStudyQuestionsAsync(Topic, 3, ["Pregunta ya existente"]);

        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        var system = body.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
        var user = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString()!;
        Assert.Contains("No inventes", system);
        Assert.Contains("La memoria local en SQLite es la fuente predeterminada.", user);
        Assert.Contains(Topic.SourceUrl, user);
        Assert.Contains("Pregunta ya existente", user);
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
    }

    [Fact]
    public async Task GenerateStudyQuestionsAsync_drops_malformed_questions()
    {
        var json = QuestionsJson("""
            {"questions":[
              {"kind":"MultipleChoice","difficulty":"Basic","prompt":"Sin respuesta válida","options":["A","B","C"],"correctOption":5,"explanation":"x"},
              {"kind":"MultipleChoice","difficulty":"Basic","prompt":"Pocas opciones","options":["A"],"correctOption":0,"explanation":"x"},
              {"kind":"Other","difficulty":"Basic","prompt":"Tipo raro","options":["A","B","C"],"correctOption":0,"explanation":"x"},
              {"kind":"MultipleChoice","difficulty":"Basic","prompt":"","options":["A","B","C"],"correctOption":0,"explanation":"x"},
              {"kind":"MultipleChoice","difficulty":"Basic","prompt":"Sin explicación","options":["A","B","C"],"correctOption":0,"explanation":" "}
            ]}
            """);
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, json));

        Assert.Empty(await client.GenerateStudyQuestionsAsync(Topic, 5, []));
    }

    [Fact]
    public async Task GenerateStudyQuestionsAsync_with_non_json_text_throws_GeminiApiException()
    {
        var client = CreateClient(new FakeHttpMessageHandler(HttpStatusCode.OK, QuestionsJson("no es json")));

        await Assert.ThrowsAsync<GeminiApiException>(() => client.GenerateStudyQuestionsAsync(Topic, 3, []));
    }

    [Fact]
    public async Task GenerateJsonAsync_sends_response_mime_type_and_the_given_schema()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, QuestionsJson("""{"puntos":["x"]}"""));
        using var schema = JsonDocument.Parse("""{"type":"OBJECT","properties":{"puntos":{"type":"ARRAY"}}}""");

        var text = await CreateClient(handler).GenerateJsonAsync(
            "Sistema", "Usuario", schema.RootElement, "a release summary");

        Assert.Equal("""{"puntos":["x"]}""", text);
        using var body = JsonDocument.Parse(handler.RequestBodies[0]!);
        var generationConfig = body.RootElement.GetProperty("generationConfig");
        Assert.Equal("application/json", generationConfig.GetProperty("responseMimeType").GetString());
        Assert.Equal("OBJECT", generationConfig.GetProperty("responseSchema").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GenerateJsonAsync_http_errors_throw_GeminiApiException_with_status(HttpStatusCode status)
    {
        var handler = new FakeHttpMessageHandler(status, """{"error":{"message":"details"}}""");
        using var schema = JsonDocument.Parse("""{"type":"OBJECT"}""");

        var ex = await Assert.ThrowsAsync<GeminiApiException>(
            () => CreateClient(handler).GenerateJsonAsync("Sistema", "Usuario", schema.RootElement, "json"));

        Assert.Equal(status, ex.StatusCode);
    }

    private static GeminiClient CreateClient(FakeHttpMessageHandler handler, string model = "gemini-test-model") =>
        new(new HttpClient(handler), TestApiKey, model);
}
