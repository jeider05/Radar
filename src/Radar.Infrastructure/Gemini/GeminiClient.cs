using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Radar.Core.Contents;
using Radar.Core.Learning;

namespace Radar.Infrastructure.Gemini;

public sealed class GeminiApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>
/// Generates Spanish explanations of release notes and grounded study questions with the Gemini API (generateContent).
/// See https://ai.google.dev/gemini-api/docs/generate-content/text-generation.
/// </summary>
public sealed class GeminiClient(HttpClient httpClient, string apiKey, string model)
{
    public const string ApiKeyVariable = "GEMINI_API_KEY";
    public const string ModelVariable = "GEMINI_MODEL";
    public const string DefaultModel = "gemini-3.8-flash";

    /// <summary>Longer release notes are truncated to keep requests (and cost) bounded.</summary>
    public const int MaxNotesLength = 20_000;

    private const string ApiBaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private const string SystemInstruction = """
        Eres un asistente que explica novedades de software a desarrolladores, con un tono educativo y claro.
        Responde siempre en español.
        Usa únicamente la información de las notas del release que te proporcionan. No inventes funcionalidades,
        cifras, fechas, versiones ni consecuencias que no aparezcan en las notas. Si algo no está claro en las notas,
        dilo explícitamente en lugar de suponerlo.
        Estructura la respuesta en texto plano, sin Markdown, con estas secciones:
        Resumen: dos o tres frases sobre qué trae esta versión.
        Novedades principales: una lista breve con guiones.
        Por qué importa: qué significa para quien usa este proyecto, solo en la medida en que las notas lo permitan.
        Sé conciso: no más de 350 palabras.
        """;

    private const string StudyQuestionsInstruction = """
        Eres un profesor que prepara preguntas de examen en español sobre Gentle AI.
        Usa únicamente los hechos confirmados que te proporcionan. No inventes características, comandos, versiones,
        respuestas ni referencias que no aparezcan en esos hechos. Si los hechos no permiten formular una pregunta fiable,
        genera menos preguntas o ninguna.
        Cada pregunta tiene una sola respuesta correcta, 3 o 4 opciones distintas y una explicación breve que justifica
        la respuesta con los hechos. Las preguntas de tipo Scenario describen una situación práctica que empieza por "Escenario:".
        Responde solo con JSON con esta forma:
        {"questions":[{"kind":"MultipleChoice|Scenario","difficulty":"Basic|Advanced","prompt":"...","options":["...","..."],"correctOption":0,"explanation":"..."}]}
        """;

    public string Model { get; } = model;

    /// <summary>HttpClient settings used by Radar for Gemini requests.</summary>
    public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>
    /// Creates a client from the GEMINI_API_KEY (required) and GEMINI_MODEL (optional) environment variables.
    /// Returns null when no API key is configured.
    /// </summary>
    public static GeminiClient? FromEnvironment(HttpClient httpClient, Func<string, string?>? getVariable = null)
    {
        getVariable ??= Environment.GetEnvironmentVariable;
        var apiKey = getVariable(ApiKeyVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        var model = getVariable(ModelVariable);
        return new GeminiClient(httpClient, apiKey.Trim(), string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim());
    }

    /// <summary>Explains the release notes of <paramref name="release"/> in Spanish. Releases without notes are rejected without a request.</summary>
    public async Task<string> ExplainReleaseAsync(string sourceName, ContentItem release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.Description))
        {
            throw new GeminiApiException("This release has no notes to explain.");
        }

        return await GenerateAsync(SystemInstruction, BuildPrompt(sourceName, release), null, "an explanation", cancellationToken);
    }

    /// <summary>
    /// Generates up to <paramref name="count"/> Spanish study questions grounded only in the topic's confirmed facts.
    /// Malformed questions are dropped, so the result can be empty when the facts do not support new questions.
    /// </summary>
    public async Task<IReadOnlyList<StudyQuestion>> GenerateStudyQuestionsAsync(
        StudyTopic topic, int count, IReadOnlyList<string> existingPrompts, CancellationToken cancellationToken = default)
    {
        var text = await GenerateAsync(
            StudyQuestionsInstruction,
            BuildStudyPrompt(topic, count, existingPrompts),
            new GenerationConfig("application/json"),
            "questions",
            cancellationToken);

        GeneratedQuestions? generated;
        try
        {
            generated = JsonSerializer.Deserialize<GeneratedQuestions>(text, JsonOptions);
        }
        catch (JsonException)
        {
            throw new GeminiApiException("Gemini returned questions in an unexpected format.");
        }

        return (generated?.Questions ?? [])
            .Select(q => ToStudyQuestion(q, topic.Id))
            .OfType<StudyQuestion>()
            .Take(count)
            .ToList();
    }

    private async Task<string> GenerateAsync(
        string systemInstruction, string prompt, GenerationConfig? generationConfig, string expected, CancellationToken cancellationToken)
    {
        var request = new GenerateContentRequest(
            new Content(null, [new Part(systemInstruction)]),
            [new Content("user", [new Part(prompt)])],
            generationConfig);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{ApiBaseUrl}/{Uri.EscapeDataString(Model)}:generateContent")
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };
        message.Headers.Add("x-goog-api-key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(message, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new GeminiApiException($"Could not reach Gemini: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateError(response.StatusCode);
            }

            GenerateContentResponse? result;
            try
            {
                result = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(JsonOptions, cancellationToken);
            }
            catch (JsonException)
            {
                throw new GeminiApiException("Gemini returned an unexpected response.", response.StatusCode);
            }

            var candidate = result?.Candidates?.FirstOrDefault();
            var text = string.Concat(candidate?.Content?.Parts?.Select(p => p.Text) ?? []).Trim();
            if (text.Length == 0)
            {
                var reason = result?.PromptFeedback?.BlockReason ?? candidate?.FinishReason ?? "no text";
                throw new GeminiApiException($"Gemini did not return {expected} ({reason}).", response.StatusCode);
            }

            return text;
        }
    }

    private static string BuildStudyPrompt(StudyTopic topic, int count, IReadOnlyList<string> existingPrompts)
    {
        var prompt = new StringBuilder()
            .AppendLine($"Tema: {topic.Title}")
            .AppendLine($"Fuente oficial: {topic.SourceUrl}")
            .AppendLine($"Referencia: {topic.SourceReference}")
            .AppendLine($"Genera como máximo {count} preguntas.")
            .AppendLine()
            .AppendLine("Hechos confirmados por la documentación oficial (única información que puedes usar):")
            .AppendLine("<<<")
            .AppendLine(topic.ConfirmedFacts)
            .AppendLine(">>>");
        if (existingPrompts.Count > 0)
        {
            prompt.AppendLine().AppendLine("Preguntas que ya existen (no las repitas ni las reformules):");
            foreach (var existing in existingPrompts)
            {
                prompt.AppendLine($"- {existing}");
            }
        }

        return prompt.ToString();
    }

    private StudyQuestion? ToStudyQuestion(GeneratedQuestion q, long topicId) =>
        Enum.TryParse<QuestionKind>(q.Kind, ignoreCase: true, out var kind)
        && Enum.TryParse<QuestionDifficulty>(q.Difficulty, ignoreCase: true, out var difficulty)
        && !string.IsNullOrWhiteSpace(q.Prompt)
        && !string.IsNullOrWhiteSpace(q.Explanation)
        && q.Options is { Count: >= 3 and <= 4 } options
        && options.All(o => !string.IsNullOrWhiteSpace(o))
        && options.Distinct().Count() == options.Count
        && q.CorrectOption is { } correct && correct >= 0 && correct < options.Count
            ? new StudyQuestion(0, topicId, kind, difficulty, q.Prompt.Trim(), options.Select(o => o.Trim()).ToList(), correct, q.Explanation.Trim(), $"Gemini {Model}")
            : null;

    private static string BuildPrompt(string sourceName, ContentItem release)
    {
        var notes = release.Description!.Trim();
        var truncated = notes.Length > MaxNotesLength;
        if (truncated)
        {
            notes = notes[..MaxNotesLength];
        }

        var prompt = new StringBuilder()
            .AppendLine($"Proyecto: {sourceName}")
            .AppendLine($"Release: {release.Title}")
            .AppendLine($"Publicado: {release.PublishedAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "desconocido"}")
            .AppendLine($"Enlace: {release.Url}")
            .AppendLine()
            .AppendLine("Notas originales del release:")
            .AppendLine("<<<")
            .AppendLine(notes)
            .AppendLine(">>>");
        if (truncated)
        {
            prompt.AppendLine("(Las notas están truncadas por su longitud; explica solo la parte incluida.)");
        }

        return prompt.ToString();
    }

    private static GeminiApiException CreateError(HttpStatusCode status) => new(status switch
    {
        HttpStatusCode.BadRequest => "Gemini rejected the request (400). Check that GEMINI_API_KEY and the model name are valid.",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"Gemini denied access ({(int)status}). Check GEMINI_API_KEY.",
        HttpStatusCode.NotFound => "The Gemini model was not found (404). Check GEMINI_MODEL.",
        HttpStatusCode.TooManyRequests => "Gemini quota or rate limit reached (429). Try again later.",
        _ => $"Gemini returned {(int)status}. Try again later.",
    }, status);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private sealed record GenerateContentRequest(
        [property: JsonPropertyName("system_instruction")] Content SystemInstruction,
        IReadOnlyList<Content> Contents,
        GenerationConfig? GenerationConfig = null);

    private sealed record GenerationConfig(string ResponseMimeType);

    private sealed record GeneratedQuestions(IReadOnlyList<GeneratedQuestion>? Questions);

    private sealed record GeneratedQuestion(
        string? Kind, string? Difficulty, string? Prompt, IReadOnlyList<string>? Options, int? CorrectOption, string? Explanation);

    private sealed record Content(string? Role, IReadOnlyList<Part>? Parts);

    private sealed record Part(string? Text);

    private sealed record Candidate(Content? Content, string? FinishReason);

    private sealed record PromptFeedback(string? BlockReason);

    private sealed record GenerateContentResponse(IReadOnlyList<Candidate>? Candidates, PromptFeedback? PromptFeedback);
}
